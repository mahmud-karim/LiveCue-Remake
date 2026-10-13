using System.Buffers;
using System.Text.Unicode;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Edge;

/// <summary>
/// Protocol 5.4: the body of a route that reads one, in the order of its four rules (W-design 4.5).
/// Nothing here runs before step E-3 has passed: only a route handler calls it.
/// </summary>
internal static class BodyReader
{
    private const int FirstBufferBytes = 4_096;

    public static async Task<BodyResult> ReadAsync(RouteContext context, TimeProvider clock)
    {
        var http = context.Http;
        var request = http.Request;
        var state = http.Features.Get<EdgeRequest>();

        // Rule 1: a declared length above the limit is refused without reading.
        var declared = request.ContentLength;
        if (declared is > ProtocolConstants.MaxRequestBodyBytes)
        {
            return Failed(RelayErrors.PayloadTooLarge);
        }

        // Rule 3: the whole body arrives within C-41, counted from the arrival of the headers.
        var left = ProtocolConstants.BodyTimeout - clock.GetElapsedTime(context.ArrivalTimestamp);
        using var limit = new CancellationTokenSource(left > TimeSpan.Zero ? left : TimeSpan.Zero, clock);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, context.Work);

        var bytes = new ArrayBufferWriter<byte>(declared is > 0 ? (int)declared.Value : FirstBufferBytes);
        var reader = request.BodyReader;
        try
        {
            // Rule 2: bytes are counted while streaming; the read stops at the first byte beyond the limit.
            while (true)
            {
                var result = await reader.ReadAsync(reading.Token).ConfigureAwait(false);
                var buffer = result.Buffer;
                if (bytes.WrittenCount + buffer.Length > ProtocolConstants.MaxRequestBodyBytes)
                {
                    reader.AdvanceTo(buffer.End);
                    return Failed(RelayErrors.PayloadTooLarge);
                }

                foreach (var segment in buffer)
                {
                    bytes.Write(segment.Span);
                }

                reader.AdvanceTo(buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }

                reading.Token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (
            limit.IsCancellationRequested
            || (context.Cancellation == RouteCancellation.DeadlineExceeded
                && context.Route.Deadline >= ProtocolConstants.BodyTimeout))
        {
            // The C-41 limit is looked at first, so it also wins a tie with a route deadline of the same
            // length, whichever of the two timers fired first. On a route whose deadline is shorter the
            // filter is false and the cancellation escapes: the edge answers 504.
            return Failed(RelayErrors.RequestTimeout);
        }
        catch (BadHttpRequestException exception)
        {
            // The server core gave up on the body (W-design K-5): too large, too slow, or framed wrongly.
            return Failed(exception.StatusCode switch
            {
                StatusCodes.Status413PayloadTooLarge => RelayErrors.PayloadTooLarge,
                StatusCodes.Status408RequestTimeout => RelayErrors.RequestTimeout,
                _ => RelayErrors.InvalidRequest,
            });
        }
        catch (ConnectionResetException)
        {
            // The caller's connection went away under the read: there is nobody to answer.
            state?.MarkConnectionLost();
            throw new OperationCanceledException(context.ClientAborted);
        }
        catch (IOException)
        {
            // Framing the server core could not read and reports as a plain I/O failure: a chunk size
            // beyond any number. It is the caller's mistake like any other bad framing, never an internal
            // error; the body stays unread, so the answer closes the connection (W-design K-4).
            return Failed(RelayErrors.InvalidRequest);
        }

        // From here on the connection holds no unread byte of this request (W-design K-4).
        state?.MarkBodyRead();

        // Rule 4: valid UTF-8 JSON, depth-limited, whose top level is an object. The JSON reader checks the
        // structure and leaves the bytes inside a string alone, so the encoding is checked here: no handler
        // ever meets a text that cannot be decoded.
        var utf8 = bytes.WrittenMemory;
        if (!Utf8.IsValid(utf8.Span) || !WireJson.TryParseObject(utf8, out var root))
        {
            return Failed(RelayErrors.InvalidJson);
        }

        var readMs = clock.GetElapsedTime(context.ArrivalTimestamp).TotalMilliseconds;
        return new BodyResult(null, utf8, root, readMs);
    }

    private static BodyResult Failed(RelayError error) => new(error, default, default, 0);
}
