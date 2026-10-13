using System.Text;
using System.Text.Json;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Answers;

/// <summary>
/// Routes 7, 8 and 9 behind one object (protocol section 8; W-design 4.9): the one slot (C-33), the
/// check order R-1 to R-8, the one deadline (C-43, counted from the arrival of the request), the runs
/// in flight by request id, and the desktop events of a request.
///
/// How a request that started can end, first match wins:
///
/// 1. route 9 cancelled its id: 409 REQUEST_CANCELLED;
/// 2. the phone's connection went away: no response (event code CLIENT_DISCONNECTED);
/// 3. the relay is stopping: no response (event code RELAY_STOPPING);
/// 4. pairing was reset: 401 UNAUTHORIZED;
/// 5. the deadline passed: 504 ASSISTANT_TIMEOUT;
/// 6. the run produced no valid output: 500 ASSISTANT_FAILED;
/// 7. otherwise 200 with the reply built by <see cref="AnswerValidator"/>.
///
/// A cancelled request is therefore never reported as a failure or as a time-out, and neither of those
/// is ever reported as cancelled.
/// </summary>
internal sealed class AnswerCoordinator : IAnswerCoordinator
{
    private const string Source = "answers";

    /// <summary>The event text of CLIENT_DISCONNECTED, which is not a wire code and has no row in the error table.</summary>
    private const string ClientDisconnectedText = "The phone disconnected before the answer was ready.";

    private readonly RelayContext _context;
    private readonly ICatalogReader _catalog;
    private readonly IAnswerBackend _backend;
    private readonly AnswerSlot _slot = new();
    private readonly RunRegistry _runs = new();

    public AnswerCoordinator(RelayContext context, ICatalogReader catalog)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalog);
        _context = context;
        _catalog = catalog;
        _backend = context.Dependencies.AnswerBackend ?? new CodexAnswerBackend(context);
    }

    public int InFlight => _slot.IsHeld ? 1 : 0;

    public bool TryCancel(string requestId) => _runs.TryCancel(requestId);

    public async Task HandleAsync(RouteContext context, AnswerKind kind)
    {
        ArgumentNullException.ThrowIfNull(context);

        // R-1.
        if (!_context.Access.Accepting)
        {
            await context.WriteErrorAsync(RelayErrors.RelayPaused).ConfigureAwait(false);
            return;
        }

        // R-2. The slot is taken here and left when the request ends for any reason.
        if (!_slot.TryEnter())
        {
            await context.WriteErrorAsync(RelayErrors.AssistantBusy).ConfigureAwait(false);
            return;
        }

        Ending ending;
        try
        {
            ending = await DecideAsync(context, kind).ConfigureAwait(false);
        }
        finally
        {
            _slot.Leave();
        }

        // The slot is free before the phone can read its answer, so its next request is never "busy"
        // because of this one.
        if (ending.Reply is { } reply)
        {
            await context.WriteJsonAsync(StatusCodes.Status200OK, reply).ConfigureAwait(false);
        }
        else if (ending.Error is { } error)
        {
            await context.WriteErrorAsync(error).ConfigureAwait(false);
        }
        else
        {
            // Nothing is written. The edge ends the connection as protocol 5.5 says.
            throw new OperationCanceledException(context.Work);
        }
    }

    /// <summary>Steps R-3 to R-8 and the desktop events. Everything but writing the response.</summary>
    private async Task<Ending> DecideAsync(RouteContext context, AnswerKind kind)
    {
        // R-3.
        var body = await context.ReadJsonBodyAsync().ConfigureAwait(false);
        if (body.Error is { } bodyError)
        {
            return Ending.Refused(bodyError);
        }

        // The body as one compact line, member order and duplicates kept: what the prompt embeds and
        // what the desktop is shown. A body that cannot be written again is not valid JSON text.
        byte[] compactBody;
        try
        {
            compactBody = WireJson.Compact(body.Root);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException)
        {
            return Ending.Refused(RelayErrors.InvalidJson);
        }

        // R-4. The shape is the one the cancel path is matched with (C-03).
        if (!TryReadRequestId(body.Root, out var requestId))
        {
            return Ending.Refused(RelayErrors.InvalidRequestId);
        }

        // R-5.
        if (!AssistantSelection.TryReadShape(body.Root, out var model, out var reasoningEffort))
        {
            return Ending.Refused(RelayErrors.InvalidAssistant);
        }

        // From here on the request is in flight under its id, bounded by C-43 as one deadline.
        using var deadline = CreateDeadline(context);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(context.Work, deadline.Token);
        using var registration = _runs.Register(requestId, run);
        var request = new Request(kind, requestId, model, reasoningEffort, compactBody, body);

        var interrupted = false;
        var natural = default(Ending);
        try
        {
            natural = await AnswerAsync(context, request, run.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
        }

        // First match wins. Closing the registration and reading "was it cancelled" is one step, so the
        // answer route 9 gave and the way this request ends always agree.
        Ending ending;
        if (registration.Complete())
        {
            ending = Ending.Refused(RelayErrors.RequestCancelled);
        }
        else
        {
            ending = context.Cancellation switch
            {
                RouteCancellation.ClientAborted => Ending.Silent(RelayCodes.ClientDisconnected, ClientDisconnectedText),
                RouteCancellation.RelayStopping => Ending.Silent(RelayErrors.RelayStopping.Code, RelayErrors.RelayStopping.Text),
                RouteCancellation.PairingRevoked => Ending.Refused(RelayErrors.Unauthorized),
                RouteCancellation.DeadlineExceeded => Ending.Refused(RelayErrors.AssistantTimeout),
                _ when deadline.IsCancellationRequested => Ending.Refused(RelayErrors.AssistantTimeout),
                _ when interrupted => Ending.Refused(RelayErrors.AssistantFailed),
                _ => natural,
            };
        }

        if (request.Started)
        {
            PublishOutcome(request, ending);
        }

        return ending;
    }

    /// <summary>
    /// Steps R-6 to R-8 when nothing interrupts them. An <see cref="OperationCanceledException"/> of
    /// <paramref name="run"/> escapes.
    /// </summary>
    private async Task<Ending> AnswerAsync(RouteContext context, Request request, CancellationToken run)
    {
        var events = _context.Events;

        // R-6.
        IReadOnlyList<ModelOffer> catalog;
        try
        {
            catalog = await _catalog.ReadAsync(run).ConfigureAwait(false);
        }
        catch (RelayException exception) when (exception.Error.Code == RelayCodes.AssistantCatalogUnavailable)
        {
            events.Publish(new AssistantNotice(events.Now, exception.Error.Code, exception.Error.Text));
            return Ending.Refused(exception.Error);
        }

        // R-7. Only the catalogue's own text of the selection is used from here on.
        if (!AssistantSelection.TryFind(catalog, request.Model, request.ReasoningEffort, out var choice))
        {
            var unavailable = RelayErrors.AssistantSelectionUnavailable;
            events.Publish(new AssistantNotice(events.Now, unavailable.Code, unavailable.Text));
            return Ending.Refused(unavailable);
        }

        request.Model = choice.Model;
        request.ReasoningEffort = choice.ReasoningEffort;
        var root = request.Body.Root;
        events.Publish(new RequestStarted(
            events.Now,
            request.RequestId,
            request.Kind,
            choice.Model,
            choice.ReasoningEffort,
            TextOf(root, "transcript"),
            TextOf(root, "partialTranscript"),
            TextOf(root, "instruction"),
            Encoding.UTF8.GetString(request.CompactBody)));
        request.Started = true;

        // R-8.
        var clock = _context.Clock;
        var before = clock.GetTimestamp();
        byte[]? output;
        try
        {
            output = await _backend.RunAsync(
                new AnswerRun(request.Kind, request.RequestId, choice.Model, choice.ReasoningEffort, request.CompactBody),
                run).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A backend that breaks in any way is a failed run, never text for the phone.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            events.Diagnostic(Source, "The answer run failed unexpectedly.", exception);
            output = null;
        }

        var timings = ExecutionTimings.From(
            codexMs: clock.GetElapsedTime(before).TotalMilliseconds,
            relayTotalMs: clock.GetElapsedTime(context.ArrivalTimestamp).TotalMilliseconds,
            requestReadMs: request.Body.ReadMs);

        return output is not null
            && AnswerValidator.TryBuildReply(request.Kind, output, choice.Model, choice.ReasoningEffort, timings, out var reply)
            ? Ending.Answered(reply, timings)
            : Ending.Refused(RelayErrors.AssistantFailed);
    }

    /// <summary>One event for the outcome of a request whose start was published.</summary>
    private void PublishOutcome(Request request, Ending ending)
    {
        var events = _context.Events;
        if (ending.Reply is { } reply)
        {
            events.Publish(new RequestCompleted(
                events.Now,
                request.RequestId,
                request.Kind,
                ending.Timings.RelayTotalMs,
                ending.Timings.CodexMs,
                request.Model,
                request.ReasoningEffort,
                Encoding.UTF8.GetString(reply)));
        }
        else
        {
            events.Publish(new RequestFailed(events.Now, request.RequestId, request.Kind, ending.Code, ending.Message));
        }
    }

    /// <summary>
    /// C-43 counted from the arrival of the request, on the relay's clock. The edge's work token carries
    /// the route deadline too; this timer makes the one deadline of protocol 8.4 hold by itself, with the
    /// body read and the catalogue check inside it.
    /// </summary>
    private CancellationTokenSource CreateDeadline(RouteContext context)
    {
        var left = ProtocolConstants.AnswerDeadline - _context.Clock.GetElapsedTime(context.ArrivalTimestamp);
        if (left > TimeSpan.Zero)
        {
            return new CancellationTokenSource(left, _context.Clock);
        }

        var passed = new CancellationTokenSource();
        passed.Cancel();
        return passed;
    }

    private static bool TryReadRequestId(JsonElement body, out string requestId)
    {
        requestId = string.Empty;
        return body.TryGetProperty("requestId", out var member)
            && AssistantSelection.TryReadText(member, out requestId)
            && RouteTable.IsRequestIdShape(requestId);
    }

    /// <summary>A member of the body when it is a string; otherwise null.</summary>
    private static string? TextOf(JsonElement body, string name) =>
        body.TryGetProperty(name, out var member) && AssistantSelection.TryReadText(member, out var text) ? text : null;

    /// <summary>One request between R-5 and its end.</summary>
    private sealed class Request(
        AnswerKind kind,
        string requestId,
        string model,
        string reasoningEffort,
        byte[] compactBody,
        BodyResult body)
    {
        public AnswerKind Kind { get; } = kind;

        public string RequestId { get; } = requestId;

        /// <summary>The request's text until R-7 passes, then the catalogue's.</summary>
        public string Model { get; set; } = model;

        public string ReasoningEffort { get; set; } = reasoningEffort;

        public byte[] CompactBody { get; } = compactBody;

        public BodyResult Body { get; } = body;

        /// <summary>True once RequestStarted was published.</summary>
        public bool Started { get; set; }
    }

    /// <summary>
    /// How a request ends: a reply (200), an error of the table, or neither, which means that no response
    /// is written. Code and Message are what a RequestFailed event carries.
    /// </summary>
    private readonly record struct Ending(
        byte[]? Reply,
        RelayError? Error,
        string Code,
        string Message,
        ExecutionTimings Timings)
    {
        public static Ending Answered(byte[] reply, ExecutionTimings timings) =>
            new(reply, null, string.Empty, string.Empty, timings);

        public static Ending Refused(RelayError error) => new(null, error, error.Code, error.Text, default);

        public static Ending Silent(string code, string message) => new(null, null, code, message, default);
    }
}
