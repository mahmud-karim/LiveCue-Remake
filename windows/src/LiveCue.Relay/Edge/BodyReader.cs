using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Edge;

/// <summary>
/// Protocol 5.4: the body of a route that reads one (W-design 4.5).
///
/// First version (foundation): rules 1, 2 and 4. Rule 3 (the body must arrive within C-41, answered
/// 408 REQUEST_TIMEOUT) needs the route deadlines and is work package WP-W01.
/// </summary>
internal static class BodyReader
{
    public static async Task<BodyResult> ReadAsync(RouteContext context, TimeProvider clock)
    {
        var request = context.Http.Request;

        // Rule 1: a declared length above the limit is refused without reading.
        if (request.ContentLength is > ProtocolConstants.MaxRequestBodyBytes)
        {
            return Failed(RelayErrors.PayloadTooLarge);
        }

        // Rule 2: bytes are counted while streaming; the read stops at the first byte beyond the limit.
        var buffer = new byte[ProtocolConstants.MaxRequestBodyBytes + 1];
        var count = 0;
        try
        {
            while (count < buffer.Length)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(count), context.Work).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }
        }
        catch (BadHttpRequestException exception)
        {
            return Failed(exception.StatusCode switch
            {
                StatusCodes.Status413PayloadTooLarge => RelayErrors.PayloadTooLarge,
                StatusCodes.Status408RequestTimeout => RelayErrors.RequestTimeout,
                _ => RelayErrors.InvalidRequest,
            });
        }

        if (count > ProtocolConstants.MaxRequestBodyBytes)
        {
            return Failed(RelayErrors.PayloadTooLarge);
        }

        // Rule 4: valid UTF-8 JSON, depth-limited, whose top level is an object.
        var utf8 = new ReadOnlyMemory<byte>(buffer, 0, count);
        if (!WireJson.TryParseObject(utf8, out var root))
        {
            return Failed(RelayErrors.InvalidJson);
        }

        var readMs = clock.GetElapsedTime(context.ArrivalTimestamp).TotalMilliseconds;
        return new BodyResult(null, utf8.ToArray(), root, readMs);
    }

    private static BodyResult Failed(RelayError error) => new(error, default, default, 0);
}
