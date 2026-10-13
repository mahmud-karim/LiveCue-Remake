using LiveCue.Relay.Answers;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// The moments of an answer request that no socket can time, on the coordinator itself: a cancel that
/// arrives in the instant the run returns, a cancel after the outcome is decided, and the order in which
/// the slot is left and the response is written. (Review item: cancellation is truthful, and a cancelled
/// request is told apart from a failure and from a time-out.)
/// </summary>
public sealed class AnswerCoordinatorTests : IDisposable
{
    private readonly AreaHarness _harness = new();
    private readonly AnswerCoordinator _coordinator;

    public AnswerCoordinatorTests()
    {
        _coordinator = new AnswerCoordinator(_harness.Context, new CatalogReader(_harness.Context));
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_cancel_that_was_answered_cancelled_is_never_followed_by_an_answer()
    {
        // The run returns a valid answer in the very moment route 9 names its id, and ignores its token.
        bool? cancelAnswer = null;
        _harness.Backend.Handler = (run, _) =>
        {
            cancelAnswer = _coordinator.TryCancel(run.RequestId);
            return Task.FromResult<byte[]?>(Bodies.ValidAssistOutput());
        };
        using var request = new RouteRequest(_harness, RouteId.Assist, Bodies.Assist());

        await _coordinator.HandleAsync(request.Context, AnswerKind.Assist);

        // Route 9 said "cancelled", so the request ends as cancelled: the answer is discarded.
        Assert.True(cancelAnswer);
        Assert.Equal(
            (409, """{"error":"The request was cancelled.","code":"REQUEST_CANCELLED"}"""),
            Assert.Single(request.Io.Responses));
        var failed = Assert.Single(_harness.Events.OfType<RequestFailed>());
        Assert.Equal(RelayCodes.RequestCancelled, failed.Code);
        Assert.Empty(_harness.Events.OfType<RequestCompleted>());
        Assert.False(_coordinator.TryCancel(Bodies.RequestId));
        Assert.Equal(0, _coordinator.InFlight);
    }

    [Fact]
    public async Task Once_the_outcome_is_decided_a_cancel_finds_nothing_and_the_slot_is_already_free()
    {
        var inFlightDuringTheRun = -1;
        _harness.Backend.Handler = (_, _) =>
        {
            inFlightDuringTheRun = _coordinator.InFlight;
            return Task.FromResult<byte[]?>(Bodies.ValidAssistOutput());
        };
        using var request = new RouteRequest(_harness, RouteId.Assist, Bodies.Assist());
        bool? lateCancel = null;
        var inFlightAtTheWrite = -1;
        var completedAtTheWrite = -1;
        request.Io.OnWrite = () =>
        {
            lateCancel = _coordinator.TryCancel(Bodies.RequestId);
            inFlightAtTheWrite = _coordinator.InFlight;
            completedAtTheWrite = _harness.Events.OfType<RequestCompleted>().Count;
        };

        await _coordinator.HandleAsync(request.Context, AnswerKind.Assist);

        // The answer was already on its way: a cancel now is told "not found", and nothing changes.
        Assert.False(lateCancel);
        Assert.Equal(200, Assert.Single(request.Io.Responses).Status);
        Assert.Empty(_harness.Events.OfType<RequestFailed>());

        // The slot was held during the run and left before the response was written, and the desktop
        // had been told the outcome by then.
        Assert.Equal(1, inFlightDuringTheRun);
        Assert.Equal(0, inFlightAtTheWrite);
        Assert.Equal(1, completedAtTheWrite);
    }

    [Fact]
    public async Task A_cancel_wins_over_every_other_cause_and_a_time_out_is_never_a_failure()
    {
        // The run is interrupted while the deadline has passed AND route 9 has cancelled: cancelled.
        _harness.Backend.Handler = async (run, ct) =>
        {
            _harness.Clock.Advance(ProtocolConstants.AnswerDeadline);
            Assert.True(ct.IsCancellationRequested);
            Assert.True(_coordinator.TryCancel(run.RequestId));
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };
        using var both = new RouteRequest(_harness, RouteId.Assist, Bodies.Assist("both"));
        await _coordinator.HandleAsync(both.Context, AnswerKind.Assist);
        Assert.Equal(409, Assert.Single(both.Io.Responses).Status);

        // The run returns nothing in the moment the deadline passes: a time-out, not a failure.
        _harness.Backend.Handler = (_, _) =>
        {
            _harness.Clock.Advance(ProtocolConstants.AnswerDeadline);
            return Task.FromResult<byte[]?>(null);
        };
        using var late = new RouteRequest(_harness, RouteId.SessionSummary, Bodies.Summary("late"));
        await _coordinator.HandleAsync(late.Context, AnswerKind.Summary);
        Assert.Equal(
            (504, """{"error":"The local assistant request timed out.","code":"ASSISTANT_TIMEOUT"}"""),
            Assert.Single(late.Io.Responses));

        // A run that fails with time to spare: a failure, not a time-out.
        _harness.Backend.Handler = (_, _) => Task.FromResult<byte[]?>(null);
        using var failing = new RouteRequest(_harness, RouteId.Assist, Bodies.Assist("failing"));
        await _coordinator.HandleAsync(failing.Context, AnswerKind.Assist);
        Assert.Equal(500, Assert.Single(failing.Io.Responses).Status);

        Assert.Equal(
            [("both", RelayCodes.RequestCancelled), ("late", RelayCodes.AssistantTimeout), ("failing", RelayCodes.AssistantFailed)],
            _harness.Events.OfType<RequestFailed>().Select(failed => (failed.RequestId, failed.Code)));
        Assert.Equal(0, _harness.Clock.ScheduledTimers);
    }

    [Fact]
    public async Task A_run_that_ends_with_a_cancellation_nobody_asked_for_is_a_failure()
    {
        _harness.Backend.Handler = (_, _) => throw new OperationCanceledException("PRIVATE DIAGNOSTIC");
        using var request = new RouteRequest(_harness, RouteId.Assist, Bodies.Assist());

        await _coordinator.HandleAsync(request.Context, AnswerKind.Assist);

        Assert.Equal(
            (500, """{"error":"The local assistant request failed.","code":"ASSISTANT_FAILED"}"""),
            Assert.Single(request.Io.Responses));
        Assert.Equal(RelayCodes.AssistantFailed, Assert.Single(_harness.Events.OfType<RequestFailed>()).Code);
        Assert.Equal(0, _coordinator.InFlight);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("stopping")]
    public async Task A_request_that_cannot_be_answered_writes_nothing_and_lets_the_cancellation_escape(string cause)
    {
        _harness.Backend.NeverReturns = true;
        using var request = new RouteRequest(_harness, RouteId.Assist, Bodies.Assist());

        var handling = _coordinator.HandleAsync(request.Context, AnswerKind.Assist);
        await _harness.Backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (cause == "client")
        {
            await request.ClientAborted.CancelAsync();
        }
        else
        {
            _harness.Access.BeginStopping();
        }

        // The edge ends the connection for this exception; the route itself writes nothing.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handling.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(request.Io.Responses);
        Assert.Equal(1, _harness.Backend.Cancelled);
        Assert.Equal(0, _coordinator.InFlight);
        Assert.Equal(
            cause == "client" ? RelayCodes.ClientDisconnected : RelayCodes.RelayStopping,
            Assert.Single(_harness.Events.OfType<RequestFailed>()).Code);
    }
}
