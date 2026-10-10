using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Hosting.Tests;

/// <summary>Endpoint resolution and the relay supervisor (architecture 3.3).</summary>
public sealed class SupervisorTests
{
    private static readonly string DataDirectory = Path.Combine(Path.GetTempPath(), "LiveCue-hosting-tests-not-used");

    [Theory]
    [InlineData("pc.example.test", "https://pc.example.test:11443/")]
    [InlineData("PC.Example.Test.", "https://pc.example.test:11443/")]
    public void A_private_endpoint_is_the_name_and_the_private_port(string dnsName, string expected)
    {
        Assert.Equal(expected, EndpointResolver.Resolve(new HostSettings(), dnsName));
    }

    [Fact]
    public void A_public_endpoint_adds_the_public_port_and_prefix()
    {
        var settings = new HostSettings { ConnectionMode = ConnectionMode.Public, PublicHttpsPort = 8443, PublicPathPrefix = "/cue" };

        Assert.Equal("https://pc.example.test:8443/cue/", EndpointResolver.Resolve(settings, "pc.example.test"));
    }

    [Theory]
    [InlineData("pc name")]
    [InlineData("pc_name.example.test")]
    [InlineData("-pc.example.test")]
    public void A_name_that_does_not_make_a_valid_endpoint_is_refused(string dnsName)
    {
        Assert.Throws<ArgumentException>(() => EndpointResolver.Resolve(new HostSettings(), dnsName));
    }

    [Fact]
    public async Task Start_composes_the_options_from_the_settings_and_forwards_events_in_order()
    {
        var created = new List<FakeRelay>();
        var settings = new InMemorySettingsStore(new HostSettings { RelayPort = 50123, ConnectionMode = ConnectionMode.Public });
        await using var supervisor = new RelaySupervisor(settings, DataDirectory, options => Track(created, new FakeRelay(options)));
        var received = new ConcurrentQueue<RelayEvent>();
        supervisor.EventReceived += (_, relayEvent) => received.Enqueue(relayEvent);

        await supervisor.StartAsync();
        var relay = Assert.Single(created);
        relay.Raise(new PhoneSeen(At(1)));
        relay.Raise(new PhoneSeen(At(2)));
        await supervisor.WhenIdleAsync();
        await supervisor.StopAsync();

        Assert.Equal(50123, relay.Options.Port);
        Assert.Equal(ConnectionMode.Public, relay.Options.Mode);
        Assert.Equal("https://pc.example.test:10000/livecue/", relay.Options.AdvertisedEndpoint);
        Assert.Equal(DataDirectory, relay.Options.DataDirectory);
        Assert.Equal(ProtocolConstants.RelayVersion, relay.Options.RelayVersion);
        Assert.Collection(
            received,
            first => Assert.IsType<RelayStarted>(first),
            second => Assert.Equal(new PhoneSeen(At(1)), second),
            third => Assert.Equal(new PhoneSeen(At(2)), third),
            last => Assert.IsType<RelayStopped>(last));
        Assert.True(relay.Disposed);
        Assert.False(supervisor.IsRunning);
    }

    [Fact]
    public async Task Overrides_replace_the_saved_port_endpoint_and_mode_for_one_run()
    {
        var created = new List<FakeRelay>();
        var overrides = new SupervisorOverrides { Port = 0, Endpoint = "https://other.example.test/", Mode = ConnectionMode.Public };
        await using var supervisor = new RelaySupervisor(
            new InMemorySettingsStore(), DataDirectory, options => Track(created, new FakeRelay(options)), overrides);

        await supervisor.StartAsync();

        var options = Assert.Single(created).Options;
        Assert.Equal(0, options.Port);
        Assert.Equal("https://other.example.test/", options.AdvertisedEndpoint);
        Assert.Equal(ConnectionMode.Public, options.Mode);
    }

    [Fact]
    public async Task A_start_that_faults_delivers_the_fault_throws_and_leaves_nothing_running()
    {
        var created = new List<FakeRelay>();
        await using var supervisor = new RelaySupervisor(
            new InMemorySettingsStore(),
            DataDirectory,
            options => Track(created, new FakeRelay(options) { StartFault = RelayFaultCode.PortInUse }));
        var received = new ConcurrentQueue<RelayEvent>();
        supervisor.EventReceived += (_, relayEvent) => received.Enqueue(relayEvent);

        var exception = await Assert.ThrowsAsync<RelayStartException>(() => supervisor.StartAsync());

        Assert.Equal(RelayFaultCode.PortInUse, exception.Code);
        Assert.Equal(RelayFaultCode.PortInUse, Assert.IsType<RelayFaulted>(Assert.Single(received)).Code);
        Assert.False(supervisor.IsRunning);
        Assert.True(Assert.Single(created).Disposed);
    }

    [Fact]
    public async Task Every_start_gets_a_new_relay_instance()
    {
        var created = new List<FakeRelay>();
        await using var supervisor = new RelaySupervisor(
            new InMemorySettingsStore(), DataDirectory, options => Track(created, new FakeRelay(options)));

        await supervisor.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => supervisor.StartAsync());
        await supervisor.StopAsync();
        await supervisor.StartAsync();

        Assert.Equal(2, created.Count);
        Assert.NotSame(created[0], created[1]);
        Assert.True(supervisor.IsRunning);
    }

    [Fact]
    public async Task A_handler_that_throws_becomes_a_diagnostic_and_later_events_still_arrive()
    {
        var created = new List<FakeRelay>();
        await using var supervisor = new RelaySupervisor(
            new InMemorySettingsStore(), DataDirectory, options => Track(created, new FakeRelay(options)));
        var received = new ConcurrentQueue<RelayEvent>();
        supervisor.EventReceived += (_, relayEvent) =>
        {
            received.Enqueue(relayEvent);
            if (relayEvent is PhoneSeen seen && seen.At == At(1))
            {
                throw new InvalidOperationException("secret detail that must never be shown");
            }
        };

        await supervisor.StartAsync();
        created[0].Raise(new PhoneSeen(At(1)));
        created[0].Raise(new PhoneSeen(At(2)));
        await supervisor.WhenIdleAsync();

        var diagnostic = Assert.Single(received.OfType<Diagnostic>());
        Assert.Equal("An event handler failed (InvalidOperationException).", diagnostic.Message);
        Assert.DoesNotContain("secret", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(new PhoneSeen(At(2)), received);
    }

    [Fact]
    public async Task With_the_real_relay_the_supervisor_binds_loopback_and_stops_cleanly()
    {
        await using var supervisor = new RelaySupervisor(
            new InMemorySettingsStore(), DataDirectory, overrides: new SupervisorOverrides { Port = 0 });
        var received = new ConcurrentQueue<RelayEvent>();
        supervisor.EventReceived += (_, relayEvent) => received.Enqueue(relayEvent);

        await supervisor.StartAsync();
        var started = Assert.IsType<RelayStarted>(Assert.Single(received));
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{started.Port}") };
        using var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));
        await supervisor.StopAsync();

        // No pairing exists yet, so the honest answer to anyone is 401; it proves Kestrel is serving.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("https://pc.example.test:11443/", started.Endpoint);
        Assert.IsType<RelayStopped>(received.Last());
    }

    [Fact]
    public void The_hosting_library_references_no_wpf_assembly()
    {
        var references = typeof(RelaySupervisor).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.Contains("LiveCue.Relay", references);
        Assert.DoesNotContain(references, name =>
            name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" or "System.Windows.Forms");
    }

    private static DateTimeOffset At(int second) => new(2026, 10, 10, 9, 0, second, TimeSpan.Zero);

    private static FakeRelay Track(List<FakeRelay> created, FakeRelay relay)
    {
        created.Add(relay);
        return relay;
    }

    /// <summary>An <see cref="IRelay"/> that binds nothing and raises what the test tells it to.</summary>
    private sealed class FakeRelay(RelayOptions options) : IRelay
    {
        private readonly Channel<RelayEvent> _events = Channel.CreateBounded<RelayEvent>(64);

        public RelayOptions Options { get; } = options;

        public RelayFaultCode? StartFault { get; init; }

        public bool Disposed { get; private set; }

        public RelayStatus Status { get; private set; } = RelayStatus.Stopped;

        public int BoundPort => Status == RelayStatus.Running ? 50000 : 0;

        public ChannelReader<RelayEvent> Events => _events.Reader;

        public void Raise(RelayEvent relayEvent) => _events.Writer.TryWrite(relayEvent);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (StartFault is { } code)
            {
                Status = RelayStatus.Faulted;
                Raise(new RelayFaulted(At(0), code, "The relay port is already in use."));
                throw new RelayStartException(code, "The relay port is already in use.");
            }

            Status = RelayStatus.Running;
            Raise(new RelayStarted(At(0), Options.AdvertisedEndpoint, 50000, Options.Mode, 2, Options.RelayVersion, false));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (Status == RelayStatus.Running)
            {
                Status = RelayStatus.Stopped;
                Raise(new RelayStopped(At(59), RelayStopReason.Requested));
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
