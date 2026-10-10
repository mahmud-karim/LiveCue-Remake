using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.Relay.Host;

namespace LiveCue.Hosting.Tests.ConsoleHost;

/// <summary>
/// The console host of architecture 3.14, called without its process, for what the foundation's host
/// does. Work package WP-W06 owns this folder and adds the tests of the test plan (section 2.4).
/// </summary>
public sealed class ConsoleHostTests
{
    private const string Endpoint = "https://pc.example.test:11443/";

    [Fact]
    public void Every_argument_is_parsed_and_an_endpoint_is_made_canonical()
    {
        Assert.True(HostArguments.TryParse(
            ["--check-only", "--show-pairing", "--events", "json", "--endpoint", "https://PC.example.test:11443",
                "--port", "0", "--data-dir", @"C:\data", "--codex", @"C:\x\codex.exe", "--lab", @"C:\asr", "--mode", "public"],
            out var parsed));

        Assert.True(parsed.CheckOnly);
        Assert.True(parsed.ShowPairing);
        Assert.True(parsed.JsonEvents);
        Assert.Equal(@"C:\data", parsed.DataDirectory);
        Assert.Equal(
            new RelayOverrides(Endpoint, 0, @"C:\x\codex.exe", @"C:\asr", ConnectionMode.Public),
            parsed.Overrides);
        Assert.True(HostArguments.TryParse([], out var none));
        Assert.Equal(new HostArguments(false, false, false, null, new RelayOverrides()), none);
    }

    [Theory]
    [InlineData("--reset-pairing")]
    [InlineData("--unknown")]
    [InlineData("--port")]
    [InlineData("--port", "47831")]
    [InlineData("--port", "80")]
    [InlineData("--port", "x")]
    [InlineData("--endpoint", "http://pc.example.test/")]
    [InlineData("--mode", "tailnet")]
    [InlineData("--events", "xml")]
    [InlineData("--data-dir")]
    public async Task An_argument_that_is_not_accepted_prints_the_usage_and_exits_2(params string[] args)
    {
        using var output = new StringWriter();

        var exitCode = await HostProgram.RunAsync(args, Fail, output, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.StartsWith("Usage: LiveCueRelayHost", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_only()
    {
        using var passed = new StringWriter();
        using var failed = new StringWriter();

        // With an endpoint the foundation's checks pass; without one its Tailscale check fails.
        var passedCode = await HostProgram.RunAsync(["--check-only", "--endpoint", Endpoint], Services(out _), passed, CancellationToken.None);
        var failedCode = await HostProgram.RunAsync(["--check-only"], Services(out var relays), failed, CancellationToken.None);

        Assert.Equal(0, passedCode);
        Assert.Equal("LiveCue Relay prerequisites passed." + Environment.NewLine, passed.ToString());
        Assert.Equal(2, failedCode);
        Assert.Equal("Tailscale is not installed in Program Files." + Environment.NewLine, failed.ToString());
        Assert.Empty(relays.Created);
    }

    [Fact]
    public async Task A_start_fault_is_exit_3()
    {
        using var output = new StringWriter();
        var services = Services(out var relays);
        relays.NextStartFault = RelayFaultCode.PortInUse;

        var exitCode = await HostProgram.RunAsync(["--endpoint", Endpoint, "--port", "50123"], services, output, CancellationToken.None);

        Assert.Equal(3, exitCode);
        Assert.Contains("Port 50123 is already in use.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_is_a_clean_stop_and_the_token_is_never_printed_by_default()
    {
        using var output = new StringWriter();
        using var cancel = new CancellationTokenSource();
        var services = Services(out var relays);

        var running = HostProgram.RunAsync(["--endpoint", Endpoint, "--port", "50123"], services, output, cancel.Token);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (relays.Created.Count == 0 || relays.Current.Status != RelayStatus.Running)
        {
            Assert.True(DateTime.UtcNow < until, "The host did not start the relay in time.");
            await Task.Delay(10);
        }

        relays.Current.Raise(new PhoneSeen(FakeRelay.Time));
        await cancel.CancelAsync();
        var exitCode = await running.WaitAsync(TimeSpan.FromSeconds(5));
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("started on 127.0.0.1:50123", text, StringComparison.Ordinal);
        Assert.Contains("PC address: " + Endpoint, text, StringComparison.Ordinal);
        Assert.Contains("iPhone seen.", text, StringComparison.Ordinal);
        Assert.Contains("Relay stopped.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeRelay.SyntheticToken, text, StringComparison.Ordinal);
        Assert.True(relays.Current.Disposed);
    }

    [Fact]
    public async Task Show_pairing_starts_no_relay_and_in_the_foundation_reports_that_no_pairing_exists()
    {
        using var data = new LiveCue.TestSupport.TempFolder();
        using var output = new StringWriter();
        var services = Services(out var relays);

        var exitCode = await HostProgram.RunAsync(
            ["--show-pairing", "--endpoint", Endpoint, "--data-dir", data.Path], services, output, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(HostProgram.NoPairingText + Environment.NewLine, output.ToString());
        Assert.Empty(relays.Created);
        Assert.Empty(data.FileNames());
    }

    [Fact]
    public async Task Show_pairing_without_an_address_says_why()
    {
        using var output = new StringWriter();

        var exitCode = await HostProgram.RunAsync(["--show-pairing"], Services(out _), output, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal("Tailscale is not installed in Program Files." + Environment.NewLine, output.ToString());
    }

    private static HostingServices Fail(HostArguments arguments) =>
        throw new InvalidOperationException("No services are composed for a usage error.");

    /// <summary>The foundation's hosting library over an in-memory store, a fake file system and fake relays.</summary>
    private static Func<HostArguments, HostingServices> Services(out FakeRelayFactory relays)
    {
        var factory = new FakeRelayFactory();
        relays = factory;
        return arguments => HostingFactory.Create(
            new SupervisorOptions(arguments.DataDirectory ?? @"C:\LiveCue-tests\data", "1.0.0", arguments.Overrides),
            new HostingOverrides
            {
                Files = new FakeHostFileSystem(),
                Settings = new InMemorySettingsStore(),
                RelayFactory = factory,
            });
    }
}
