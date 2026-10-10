using LiveCue.Relay.Catalog;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Answers;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Catalog;

/// <summary>
/// The catalogue exchange with <c>codex app-server</c> (protocol 12.1; statement P2-044) against a
/// scripted line session: what is written, what is kept of the reply, and that every failure is a failed
/// read with the session disposed, which is what kills the process. Nothing is started.
/// </summary>
public sealed class CodexCatalogSourceTests : IDisposable
{
    private const string Initialize =
        """{"id":1,"method":"initialize","params":{"clientInfo":{"name":"livecue_catalog","title":"LiveCue","version":"1.0.0"}}}""";

    private const string Initialized = """{"method":"initialized"}""";

    private const string ModelList = """{"id":2,"method":"model/list","params":{"limit":100,"includeHidden":false}}""";

    private const string InitializeReply = """{"id":1,"result":{"userAgent":"codex"}}""";

    private static readonly CancellationToken None = CancellationToken.None;

    private readonly AreaHarness _harness = new();
    private readonly CodexCatalogSource _source;

    public CodexCatalogSourceTests()
    {
        _source = new CodexCatalogSource(_harness.Context);
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task The_exchange_writes_the_three_lines()
    {
        var session = Session("""{"id":2,"result":{"data":[{"model":"gpt-a","displayName":"A","hidden":false,"supportedReasoningEfforts":[{"reasoningEffort":"low","description":"quick"},{"reasoningEffort":"high"}]}]}}""");

        var entries = await _source.ReadAsync(None);

        Assert.Equal([Initialize, Initialized, ModelList], session.Written);
        Assert.True(session.Disposed);
        var entry = Assert.Single(entries);
        Assert.Equal(new { Model = "gpt-a", DisplayName = (string?)"A", Hidden = false }, new { entry.Model, entry.DisplayName, entry.Hidden });
        Assert.Equal(["low", "high"], entry.ReasoningEfforts);

        // One process, as protocol 12.1 and 12.6 say: the fixed arguments, the allowlisted environment,
        // at most C-24 of output, the per-attempt limit, inside the kill-on-close job.
        var request = Assert.Single(_harness.Processes.Requests);
        Assert.Equal(RelayTestHost.FakeCodexPath, request.FileName);
        Assert.Equal(["app-server", "--listen", "stdio://"], request.Arguments);
        Assert.Equal(EnvironmentPolicy.CodexAllowlist, request.Environment);
        Assert.Equal(1_048_576, request.MaxStandardOutputBytes);
        Assert.Equal(TimeSpan.FromSeconds(12), request.Timeout);
        Assert.True(request.KillWithApplication);
        Assert.Null(request.StandardInput);
        Assert.Equal(0, _harness.Clock.ScheduledTimers);
    }

    [Fact]
    public async Task A_cursor_yields_the_entries_received()
    {
        var session = Session("""{"id":2,"result":{"data":[{"model":"gpt-a"},{"model":"gpt-b"}],"nextCursor":"page-2"}}""");

        var entries = await _source.ReadAsync(None);

        Assert.Equal(["gpt-a", "gpt-b"], entries.Select(entry => entry.Model));

        // No further page is asked for.
        Assert.Equal(3, session.Written.Count);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task An_entry_without_a_model_is_skipped()
    {
        Session(
            """{"id":2,"result":{"data":["""
            + """{"displayName":"No model"},"""
            + """{"model":7,"displayName":"A number"},"""
            + """{"model":null},"""
            + "\"gpt-text\","
            + """null,"""
            + """{"model":"gpt-kept","displayName":7,"hidden":null,"supportedReasoningEfforts":[{"reasoningEffort":"low"},{"reasoningEffort":3},{"other":"x"},"medium",null]},"""
            + """{"model":"gpt-bare"}"""
            + """]}}""");

        var entries = await _source.ReadAsync(None);

        Assert.Equal(["gpt-kept", "gpt-bare"], entries.Select(entry => entry.Model));
        Assert.Null(entries[0].DisplayName);
        Assert.False(entries[0].Hidden);
        Assert.Equal(["low"], entries[0].ReasoningEfforts);
        Assert.Null(entries[1].DisplayName);
        Assert.Empty(entries[1].ReasoningEfforts);
    }

    [Theory]
    [InlineData("""{"model":"gpt-a"}""", false)]
    [InlineData("""{"model":"gpt-a","hidden":false}""", false)]
    [InlineData("""{"model":"gpt-a","hidden":null}""", false)]
    [InlineData("""{"model":"gpt-a","hidden":true}""", true)]
    [InlineData("""{"model":"gpt-a","hidden":"false"}""", true)]
    [InlineData("""{"model":"gpt-a","hidden":1}""", true)]
    [InlineData("""{"model":"gpt-a","hidden":{}}""", true)]
    public async Task Hidden_is_true_unless_it_is_absent_false_or_null(string entryJson, bool expected)
    {
        Session("""{"id":2,"result":{"data":[""" + entryJson + "]}}");

        Assert.Equal(expected, Assert.Single(await _source.ReadAsync(None)).Hidden);
    }

    [Fact]
    public async Task Lines_that_are_not_the_awaited_reply_are_skipped()
    {
        var session = new FakeProcessSession
        {
            OnLine = line => line switch
            {
                Initialize =>
                [
                    "codex is starting…",
                    string.Empty,
                    "[1, 2, 3]",
                    """{"method":"sessionConfigured","params":{"id":1}}""",
                    """{"id":"1","result":"an id that is text"}""",
                    """{"id":2,"result":{"data":[{"model":"gpt-too-early"}]}}""",
                    InitializeReply,
                ],
                ModelList =>
                [
                    "{ not json",
                    """{"id":1,"result":{}}""",
                    """{"id":2.5,"result":{"data":[]}}""",
                    """{"id":2,"result":{"data":[{"model":"gpt-a"}]}}""",
                    """{"id":2,"result":{"data":[{"model":"gpt-too-late"}]}}""",
                ],
                _ => [],
            },
        };
        _harness.Processes.EnqueueSession(session);

        var entries = await _source.ReadAsync(None);

        Assert.Equal("gpt-a", Assert.Single(entries).Model);
        Assert.True(session.Disposed);
    }

    [Theory]
    [InlineData("overflow")]
    [InlineData("silence")]
    [InlineData("early-exit")]
    [InlineData("early-exit-before-the-first-reply")]
    [InlineData("first-reply-is-an-error")]
    [InlineData("second-reply-is-an-error")]
    [InlineData("second-reply-has-a-null-error")]
    [InlineData("data-is-not-an-array")]
    [InlineData("no-result")]
    [InlineData("result-is-not-an-object")]
    [InlineData("process-timeout")]
    public async Task Overflow_silence_and_early_exit_fail_and_dispose_the_session(string situation)
    {
        var session = new FakeProcessSession();
        session.OnLine = line => (situation, line) switch
        {
            ("early-exit-before-the-first-reply", Initialize) => End(session),
            ("first-reply-is-an-error", Initialize) => ["""{"id":1,"error":{"code":-32600,"message":"PRIVATE DIAGNOSTIC"}}"""],
            (_, Initialize) => [InitializeReply],
            ("overflow", ModelList) => Fail(session, ProcessOutcome.OutputLimitExceeded),
            ("process-timeout", ModelList) => Fail(session, ProcessOutcome.TimedOut),
            ("early-exit", ModelList) => End(session),
            ("second-reply-is-an-error", ModelList) => ["""{"id":2,"error":{"code":-32000,"message":"PRIVATE DIAGNOSTIC"}}"""],
            ("second-reply-has-a-null-error", ModelList) => ["""{"id":2,"error":null,"result":{"data":[{"model":"gpt-a"}]}}"""],
            ("data-is-not-an-array", ModelList) => ["""{"id":2,"result":{"data":{"model":"gpt-a"}}}"""],
            ("no-result", ModelList) => ["""{"id":2}"""],
            ("result-is-not-an-object", ModelList) => ["""{"id":2,"result":[{"model":"gpt-a"}]}"""],
            _ => [],
        };
        _harness.Processes.EnqueueSession(session);

        var read = _source.ReadAsync(None);
        if (situation == "silence")
        {
            // No reply to model/list: only the per-attempt limit of C-44 ends the attempt.
            await Waiting.UntilAsync(() => session.Written.Count == 3);
            _harness.Clock.Advance(ProtocolConstants.CatalogAttemptTimeout - TimeSpan.FromMilliseconds(1));
            await Task.Delay(50);
            Assert.False(read.IsCompleted);
            _harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        var failure = await Assert.ThrowsAsync<CatalogReadException>(() => read.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal("The catalogue could not be read.", failure.Message);
        Assert.Null(failure.InnerException);
        Assert.True(session.Disposed);
        Assert.Equal(0, _harness.Clock.ScheduledTimers);
    }

    [Fact]
    public async Task A_process_that_cannot_be_started_is_a_failed_read()
    {
        // No session is queued: the fake runner fails the start, as the real one does for a missing file.
        await Assert.ThrowsAsync<CatalogReadException>(() => _source.ReadAsync(None));

        Assert.Single(_harness.Processes.Requests);
        Assert.Equal(0, _harness.Clock.ScheduledTimers);
    }

    [Fact]
    public async Task A_cancelled_read_is_a_cancellation_and_the_session_is_disposed()
    {
        var session = new FakeProcessSession { OnLine = line => line == Initialize ? [InitializeReply] : [] };
        _harness.Processes.EnqueueSession(session);
        using var leaving = new CancellationTokenSource();

        var read = _source.ReadAsync(leaving.Token);
        await Waiting.UntilAsync(() => session.Written.Count == 3);
        await leaving.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(session.Disposed);
        Assert.Equal(0, _harness.Clock.ScheduledTimers);
    }

    [Fact]
    public async Task The_relay_version_is_written_as_a_json_string()
    {
        using var harness = new AreaHarness();
        var odd = harness.Options with { RelayVersion = "1.0.0-\"beta\"\\é" };
        var context = new LiveCue.Relay.Core.RelayContext(
            odd, harness.Context.Dependencies, harness.Events, harness.Access, harness.Processes, RelayTestHost.Endpoint);
        var session = new FakeProcessSession { OnLine = _ => [InitializeReply, """{"id":2,"result":{"data":[]}}"""] };
        harness.Processes.EnqueueSession(session);

        var entries = await new CodexCatalogSource(context).ReadAsync(None);

        Assert.Empty(entries);
        Assert.Equal(
            """{"id":1,"method":"initialize","params":{"clientInfo":{"name":"livecue_catalog","title":"LiveCue","version":"1.0.0-\"beta\"\\é"}}}""",
            session.Written[0]);
    }

    /// <summary>A session that answers the two requests; <paramref name="modelListReply"/> answers the second.</summary>
    private FakeProcessSession Session(string modelListReply)
    {
        var session = new FakeProcessSession
        {
            OnLine = line => line switch
            {
                Initialize => [InitializeReply],
                ModelList => [modelListReply],
                _ => [],
            },
        };
        _harness.Processes.EnqueueSession(session);
        return session;
    }

    private static string[] End(FakeProcessSession session)
    {
        session.End();
        return [];
    }

    private static string[] Fail(FakeProcessSession session, ProcessOutcome outcome)
    {
        session.FailNextRead = outcome;
        return [];
    }
}
