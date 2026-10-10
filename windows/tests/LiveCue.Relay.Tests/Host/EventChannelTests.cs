using LiveCue.Relay.Diagnostics;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Host;

/// <summary>The bounded event channel and the diagnostic sink (W-design W-01, architecture 3.4 and 3.10).</summary>
public sealed class EventChannelTests
{
    private const int Capacity = ProtocolConstants.EventChannelCapacity;

    [Fact]
    public void A_full_channel_drops_the_oldest_and_counts()
    {
        var clock = new ManualTimeProvider();
        var channel = new EventChannel(clock);
        var start = clock.GetUtcNow();

        for (var index = 0; index <= Capacity; index++)
        {
            channel.Publish(new PhoneSeen(start.AddSeconds(index)));
        }

        // One more than fits: the oldest is gone, the order of the rest is kept.
        Assert.Equal(1, channel.Dropped);
        Assert.Equal(Capacity, channel.Reader.Count);
        Assert.True(channel.Reader.TryRead(out var oldestKept));
        Assert.Equal(new PhoneSeen(start.AddSeconds(1)), oldestKept);
        Assert.True(channel.Reader.TryRead(out var next));
        Assert.Equal(new PhoneSeen(start.AddSeconds(2)), next);
    }

    [Fact]
    public void Publishing_never_blocks_and_never_throws_however_full_the_channel_is()
    {
        var channel = new EventChannel(new ManualTimeProvider());

        Parallel.For(0, Capacity * 3, _ => channel.Publish(new PhoneSeen(DateTimeOffset.UnixEpoch)));
        channel.Publish(null!);

        Assert.Equal(Capacity, channel.Reader.Count);
        Assert.Equal(Capacity * 2, channel.Dropped);
    }

    [Fact]
    public void A_diagnostic_carries_the_fixed_text_and_the_exception_type_name_only()
    {
        var clock = new ManualTimeProvider();
        var channel = new EventChannel(clock);

        channel.Diagnostic("edge", "A request failed unexpectedly.", new InvalidOperationException("secret detail"));
        channel.Diagnostic("edge", "Something else.");

        Assert.True(channel.Reader.TryRead(out var first));
        Assert.True(channel.Reader.TryRead(out var second));
        Assert.Equal(
            new Diagnostic(clock.GetUtcNow(), "edge", "A request failed unexpectedly. (InvalidOperationException)"),
            first);
        Assert.Equal(new Diagnostic(clock.GetUtcNow(), "edge", "Something else."), second);
        Assert.Equal(clock.GetUtcNow(), channel.Now);
    }

    [Fact]
    public void Publishing_after_completion_does_nothing_and_does_not_throw()
    {
        var channel = new EventChannel(new ManualTimeProvider());

        channel.Complete();
        channel.Publish(new PhoneSeen(DateTimeOffset.UnixEpoch));
        channel.Diagnostic("edge", "Too late.");
        channel.Complete();

        Assert.False(channel.Reader.TryRead(out _));
        Assert.True(channel.Reader.Completion.IsCompleted);
        Assert.False(channel.ReportDrops());
    }

    [Fact]
    public void Drops_are_reported_as_one_diagnostic_with_a_count_and_not_again()
    {
        var clock = new ManualTimeProvider();
        var channel = new EventChannel(clock);

        // Nothing was dropped: nothing to report.
        channel.Publish(new PhoneSeen(clock.GetUtcNow()));
        Assert.False(channel.ReportDrops());

        for (var index = 0; index < Capacity + 6; index++)
        {
            channel.Publish(new PhoneSeen(clock.GetUtcNow()));
        }

        Assert.Equal(7, channel.Dropped);
        Assert.True(channel.ReportDrops());

        // The report is itself an event: in a channel that is still full it pushed one more out, and
        // that one is not a reason for another report.
        Assert.Equal(8, channel.Dropped);
        Assert.False(channel.ReportDrops());
        Assert.False(channel.ReportDrops());

        RelayEvent? last = null;
        while (channel.Reader.TryRead(out var relayEvent))
        {
            last = relayEvent;
        }

        Assert.Equal(
            new Diagnostic(clock.GetUtcNow(), "events", "7 relay events were dropped because the host did not read them in time."),
            last);

        // After the host has caught up, later drops are reported with their own count.
        for (var index = 0; index < Capacity + 3; index++)
        {
            channel.Publish(new PhoneSeen(clock.GetUtcNow()));
        }

        Assert.True(channel.ReportDrops());
        Assert.False(channel.ReportDrops());
    }

    [Fact]
    public async Task A_running_relay_reports_dropped_events_once_per_interval()
    {
        // Through the whole relay, with nobody reading its events until the end.
        using var data = new TempFolder();
        using var temp = new TempFolder();
        var clock = new ManualTimeProvider();
        var files = new InMemoryPairingFiles();
        var options = new RelayOptions(
            0, RelayTestHost.Endpoint, ConnectionMode.Private, data.Path, null, null, 47842, RelayTestHost.RelayVersion, false);
        var dependencies = new RelayDependencies
        {
            Clock = clock,
            Processes = new FakeProcessRunner(),
            TempRoot = temp.Path,
            PairingProtector = new FakePairingProtector(),
            PairingFiles = files,
            SelfCheckClient = new FakeSelfCheckClient(),
            CatalogSource = new FakeCatalogSource(),
            AnswerBackend = new FakeAnswerBackend(),
            LabRuntime = new FakeLabRuntime(),
            LabSocketConnector = new FakeLabSocketConnector(),
        };
        await using var relay = RelayFactory.Create(options, dependencies);
        await relay.StartAsync(CancellationToken.None);
        var payload = await ((IPairingExport)relay).GetPairingPayloadAsync(CancellationToken.None);
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var token = document.RootElement.GetProperty("token").GetString()!;
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{relay.BoundPort}") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", TestTokens.Bearer(token));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-LiveCue-Protocol", "2");

        // RelayStarted, then one PhoneSeen per request: ten more than the channel holds.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, Capacity + 9),
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (_, cancel) =>
            {
                using var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative), cancel);
                Assert.True(response.IsSuccessStatusCode);
            });

        // One interval before, nothing is said; at the interval, one diagnostic.
        clock.Advance(ProtocolConstants.DroppedEventReportInterval - TimeSpan.FromSeconds(1));
        Assert.Equal(Capacity, relay.Events.Count);
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.Advance(ProtocolConstants.DroppedEventReportInterval * 3);

        var events = new List<RelayEvent>();
        while (relay.Events.TryRead(out var relayEvent))
        {
            events.Add(relayEvent);
        }

        var report = Assert.Single(events.OfType<Diagnostic>());
        Assert.Equal("events", report.Source);
        Assert.Equal("10 relay events were dropped because the host did not read them in time.", report.Message);
        Assert.Same(report, events[^1]);
    }
}
