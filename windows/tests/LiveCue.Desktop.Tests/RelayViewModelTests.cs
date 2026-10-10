using LiveCue.Desktop.Controls;
using LiveCue.Desktop.ViewModels;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests;

/// <summary>The relay and phone state machines of architecture 3.12, driven by scripted relay events.</summary>
public sealed class RelayViewModelTests
{
    private const string PortInUse =
        "The relay port is already in use. Close the other LiveCue relay, then click Start relay.";

    [Fact]
    public async Task At_launch_the_relay_is_stopped_and_nothing_is_known_about_a_phone()
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;

        Assert.Equal(RelayState.Stopped, relay.State);
        Assert.Equal("Stopped", relay.StatusText);
        Assert.Equal("Stopped", relay.ShortStatusText);
        Assert.Equal(StatusKind.Neutral, relay.StatusKind);
        Assert.Equal("iPhone not connected", relay.PhoneText);
        Assert.Equal(StatusKind.Neutral, relay.PhoneKind);
        Assert.Equal("Shown when the relay is running", relay.PcAddressText);
        Assert.False(relay.HasPcAddress);
        Assert.True(relay.ShowsStart);
        Assert.True(relay.StartCommand.CanExecute(null));
        Assert.False(relay.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_relay_shows_ready_the_pc_address_and_one_activity_line()
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;

        await relay.StartCommand.ExecuteAsync(null);

        Assert.Equal(RelayState.Ready, relay.State);
        Assert.Equal("Ready", relay.StatusText);
        Assert.Equal(StatusKind.Success, relay.StatusKind);
        Assert.Equal("https://pc.example.test:11443/", relay.PcAddress);
        Assert.False(relay.ShowsStart);
        Assert.False(relay.StartCommand.CanExecute(null));
        Assert.True(relay.StopCommand.CanExecute(null));
        var entry = Assert.Single(app.Services.Activity.Entries);
        Assert.Equal("Relay started. Waiting for your iPhone.", entry.Message);
        Assert.Equal("09:14:02", entry.TimeText);
    }

    [Fact]
    public async Task In_public_mode_the_start_adds_the_line_about_the_vpn()
    {
        await using var app = new ScriptedApp(new HostSettings { ConnectionMode = ConnectionMode.Public });
        var relay = app.Services.Shell.Relay;

        await relay.StartCommand.ExecuteAsync(null);

        Assert.Equal("https://pc.example.test:10000/livecue/", relay.PcAddress);
        Assert.Equal(
            "Public mode: your iPhone needs internet, not the Tailscale VPN. Pair using this PC address.",
            app.Services.Activity.Entries[1].Message);
    }

    [Fact]
    public async Task A_phone_is_connected_for_fifteen_seconds_after_its_last_request_and_idle_after_that()
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;
        await relay.StartCommand.ExecuteAsync(null);

        app.Script.PhoneSeen();
        await app.DeliverAsync();
        Assert.Equal(PhonePresence.Connected, relay.Presence);
        Assert.Equal("iPhone connected", relay.PhoneText);
        Assert.Equal(StatusKind.Success, relay.PhoneKind);

        app.Clock.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(PhonePresence.Connected, relay.Presence);

        // A request at 14 s restarts the 15 s; without it the phone would turn idle at 15 s.
        app.Script.PhoneSeen();
        await app.DeliverAsync();
        app.Clock.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(PhonePresence.Connected, relay.Presence);

        app.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(PhonePresence.Idle, relay.Presence);
        Assert.Equal("iPhone idle", relay.PhoneText);
        Assert.Equal(StatusKind.Warning, relay.PhoneKind);

        // Idle never falls back to "not connected" by timeout.
        app.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(PhonePresence.Idle, relay.Presence);

        app.Script.PhoneSeen();
        await app.DeliverAsync();
        Assert.Equal(PhonePresence.Connected, relay.Presence);
    }

    [Fact]
    public async Task Stop_relay_returns_to_stopped_resets_the_phone_and_keeps_the_address_and_the_log()
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;
        await relay.StartCommand.ExecuteAsync(null);
        app.Script.PhoneSeen();
        await app.DeliverAsync();

        await relay.StopCommand.ExecuteAsync(null);

        Assert.Equal(RelayState.Stopped, relay.State);
        Assert.Equal("Stopped", relay.StatusText);
        Assert.Equal(PhonePresence.NotConnected, relay.Presence);
        Assert.Equal("https://pc.example.test:11443/", relay.PcAddress);
        Assert.Single(app.Services.Activity.Entries);
        Assert.True(relay.StartCommand.CanExecute(null));
        Assert.False(app.Services.Supervisor.IsRunning);

        // A phone timer left over from the stopped run must not turn the phone idle.
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PhonePresence.NotConnected, relay.Presence);
    }

    [Fact]
    public async Task A_port_in_use_fault_returns_the_stopped_layout_and_keeps_its_text()
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;
        app.Script.NextStartFault = RelayFaultCode.PortInUse;

        await relay.StartCommand.ExecuteAsync(null);

        Assert.Equal(RelayState.Faulted, relay.State);
        Assert.Equal(PortInUse, relay.StatusText);
        Assert.Equal("Cannot start yet", relay.ShortStatusText);
        Assert.Equal(StatusKind.Danger, relay.StatusKind);
        Assert.True(relay.ShowsStart);
        Assert.True(relay.StartCommand.CanExecute(null));
        Assert.False(relay.StopCommand.CanExecute(null));
        Assert.Equal(PortInUse, Assert.Single(app.Services.Activity.Entries).Message);
        Assert.False(app.Services.Supervisor.IsRunning);
    }

    [Theory]
    [InlineData(RelayFaultCode.StartFailed)]
    [InlineData(RelayFaultCode.InvalidEndpoint)]
    [InlineData(RelayFaultCode.PairingStoreUnreadable)]
    [InlineData(RelayFaultCode.Internal)]
    public async Task Any_other_start_fault_reads_could_not_start_the_relay(RelayFaultCode code)
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;
        app.Script.NextStartFault = code;

        await relay.StartCommand.ExecuteAsync(null);

        Assert.Equal(RelayState.Faulted, relay.State);
        Assert.Equal("Could not start the relay.", relay.StatusText);
    }

    [Fact]
    public async Task The_next_successful_start_clears_the_fault_text_and_the_log_and_is_a_new_relay()
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;
        app.Script.NextStartFault = RelayFaultCode.PortInUse;
        await relay.StartCommand.ExecuteAsync(null);
        var faulted = app.Script.Current;

        await relay.StartCommand.ExecuteAsync(null);

        Assert.Equal(RelayState.Ready, relay.State);
        Assert.Null(relay.FaultText);
        Assert.Equal("Ready", relay.StatusText);
        Assert.Equal("Relay started. Waiting for your iPhone.", Assert.Single(app.Services.Activity.Entries).Message);
        Assert.NotSame(faulted, app.Script.Current);
        Assert.Equal(2, app.Script.Created);
    }

    [Fact]
    public async Task A_phone_seen_event_is_ignored_unless_the_relay_is_ready()
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;
        await relay.StartCommand.ExecuteAsync(null);
        var stoppedRelay = app.Script.Current!;
        await relay.StopCommand.ExecuteAsync(null);

        // Nothing listens to a stopped relay any more; raising on it must change nothing.
        stoppedRelay.Raise(new PhoneSeen(app.Clock.GetUtcNow()));

        Assert.Equal(PhonePresence.NotConnected, relay.Presence);
    }

    [Fact]
    public async Task A_diagnostic_is_logged_with_its_prefix()
    {
        await using var app = new ScriptedApp();
        await app.Services.Shell.Relay.StartCommand.ExecuteAsync(null);

        app.Script.Diagnostic("An event handler failed (InvalidOperationException).");
        await app.DeliverAsync();

        Assert.Equal(
            "Relay diagnostic: An event handler failed (InvalidOperationException).",
            app.Services.Activity.Entries[^1].Message);
    }

    [Fact]
    public async Task State_changes_are_announced_to_the_view()
    {
        await using var app = new ScriptedApp();
        var relay = app.Services.Shell.Relay;
        var changed = new List<string?>();
        relay.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        await relay.StartCommand.ExecuteAsync(null);

        Assert.Contains(nameof(RelayViewModel.State), changed);
        Assert.Contains(nameof(RelayViewModel.StatusText), changed);
        Assert.Contains(nameof(RelayViewModel.ShortStatusText), changed);
        Assert.Contains(nameof(RelayViewModel.StatusKind), changed);
        Assert.Contains(nameof(RelayViewModel.ShowsStart), changed);
        Assert.Contains(nameof(RelayViewModel.IsReady), changed);
        Assert.Contains(nameof(RelayViewModel.PcAddressText), changed);
    }
}
