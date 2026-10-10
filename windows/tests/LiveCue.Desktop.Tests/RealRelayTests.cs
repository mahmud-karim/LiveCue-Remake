using System.Net;
using System.Net.NetworkInformation;
using LiveCue.Desktop.ViewModels;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Tests;

/// <summary>
/// The window's own wiring with nothing scripted: the real supervisor and the real relay, on a port
/// the system picks. This is what "Start relay" does in the app.
/// </summary>
public sealed class RealRelayTests
{
    [Fact]
    public async Task Start_relay_really_starts_the_in_process_relay_and_stop_relay_releases_it()
    {
        await using var services = AppServices.Create(
            new ImmediateDispatcher(),
            new InMemorySettingsStore(),
            Path.Combine(Path.GetTempPath(), "LiveCue-desktop-tests-not-used"),
            createRelay: null,
            TimeProvider.System,
            new SupervisorOverrides { Port = 0 });
        var relay = services.Shell.Relay;

        await relay.StartCommand.ExecuteAsync(null);

        Assert.Equal(RelayState.Ready, relay.State);
        Assert.Equal("Ready", relay.StatusText);
        Assert.Equal("https://pc.example.test:11443/", relay.PcAddress);
        Assert.True(services.Supervisor.IsRunning);

        // The relay is on loopback only, on some port that is not the previous LiveCue's.
        var port = services.Supervisor.BoundPort;
        Assert.InRange(port, 1024, 65535);
        Assert.NotEqual(47831, port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));

        // No pairing exists yet, so the honest answer to anyone is the protocol's 401.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            """{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}""",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(PhonePresence.NotConnected, relay.Presence);

        await relay.StopCommand.ExecuteAsync(null);

        Assert.Equal(RelayState.Stopped, relay.State);
        Assert.False(services.Supervisor.IsRunning);
        Assert.DoesNotContain(
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners(),
            endpoint => endpoint.Port == port && IPAddress.IsLoopback(endpoint.Address));
    }

    [Fact]
    public void The_desktop_assembly_is_named_for_the_remake_and_uses_the_relay_only_through_hosting()
    {
        var assembly = typeof(App).Assembly;

        Assert.Equal("LiveCueRemake", assembly.GetName().Name);
        Assert.Contains(assembly.GetReferencedAssemblies(), name => name.Name == "LiveCue.Hosting");
    }
}
