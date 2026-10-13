using System.Reflection;
using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Pairing;

/// <summary>
/// The Pairing page's view model over a <see cref="FakeSession"/> (test plan 2.8; W-design 5.6; screens
/// section 3): the QR machine, its hints, its countdown, the gate on "Show QR" and the verification
/// notice. Every matrix here is a synthetic fixture.
/// </summary>
public sealed class PairingViewModelTests
{
    [Fact]
    public void State_cases()
    {
        var parts = new PairingParts();
        var pairing = parts.Pairing;
        var changed = new List<string?>();
        pairing.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        // Before any QR in this run.
        Assert.Equal(QrState.Empty, pairing.QrState);
        Assert.Equal(QrHiddenReason.None, pairing.HiddenReason);
        Assert.Equal(QrTileFace.RelayNotRunning, pairing.TileFace);
        Assert.Equal("Pair your iPhone once. Restarts and showing this QR keep existing pairings valid.", pairing.Hint);
        Assert.Equal(string.Empty, pairing.CountdownText);
        Assert.False(pairing.IsCountdownWarning);
        Assert.Equal("hidden", pairing.QrAutomationStatus);
        Assert.Equal(string.Empty, pairing.PcAddress);
        Assert.False(pairing.CanHideQr);

        // The relay runs and the connection is set up: the tile offers the QR.
        parts.Run();
        Assert.Equal(QrTileFace.Placeholder, pairing.TileFace);
        Assert.Equal("Show the QR to pair an iPhone", pairing.PlaceholderLine);
        Assert.Equal(PairingParts.Endpoint, pairing.PcAddress);

        // Show QR: the tile is busy while the session works, then the matrix arrives.
        parts.Session.Gate = new TaskCompletionSource();
        pairing.ShowQrCommand.Execute(null);
        Assert.Equal(1, parts.Session.ShowPairingCalls);
        Assert.Equal(QrTileFace.Generating, pairing.TileFace);
        Assert.False(pairing.CanResetPairing);
        Assert.False(pairing.ResetPairingCommand.CanExecute(null));

        changed.Clear();
        var matrix = parts.ProduceQr();
        parts.Session.Gate.SetResult();
        parts.Session.Gate = null;

        Assert.Equal(QrState.Shown, pairing.QrState);
        Assert.Equal(QrTileFace.Code, pairing.TileFace);
        Assert.Same(matrix, pairing.QrModules);
        Assert.Equal(
            "On iPhone: Pair Windows PC > Scan PC QR code, then Verify and pair. QR hides after 2 minutes.",
            pairing.Hint);
        Assert.Equal("Hides in 2:00", pairing.CountdownText);
        Assert.Equal("shown, hides in 2 minutes", pairing.QrAutomationStatus);
        Assert.True(pairing.CanHideQr);
        Assert.True(pairing.HideQrCommand.CanExecute(null));
        Assert.True(pairing.CanResetPairing);
        Assert.Null(pairing.VerifyNotice);
        Assert.Equal(AppPage.Pairing, parts.Navigator.Current);
        Assert.Contains(nameof(IPairingViewModel.QrModules), changed);
        Assert.Contains(nameof(IPairingViewModel.TileFace), changed);
        Assert.Contains(nameof(IPairingViewModel.Hint), changed);
        Assert.Contains(nameof(IPairingViewModel.CountdownText), changed);

        // The countdown, told once a tick; a warning in the last 30 seconds.
        parts.Clock.Advance(TimeSpan.FromSeconds(18));
        Assert.Equal("Hides in 1:42", pairing.CountdownText);
        Assert.Equal("shown, hides in 1 minute 42 seconds", pairing.QrAutomationStatus);
        Assert.False(pairing.IsCountdownWarning);
        parts.Clock.Advance(TimeSpan.FromSeconds(72));
        Assert.Equal("Hides in 0:30", pairing.CountdownText);
        Assert.True(pairing.IsCountdownWarning);
        Assert.Equal(QrState.Shown, pairing.QrState);

        // At zero the QR hides itself.
        parts.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(QrState.Hidden, pairing.QrState);
        Assert.Equal(QrHiddenReason.Timer, pairing.HiddenReason);
        Assert.Equal(QrTileFace.Placeholder, pairing.TileFace);
        Assert.Equal("QR hidden for privacy. Existing pairing remains valid.", pairing.Hint);
        Assert.Equal(string.Empty, pairing.CountdownText);
        Assert.False(pairing.IsCountdownWarning);
        Assert.Equal("hidden", pairing.QrAutomationStatus);
        Assert.False(pairing.CanHideQr);

        // Shown again, the same way; hidden by the button.
        parts.ProduceQr();
        Assert.Equal("Hides in 2:00", pairing.CountdownText);
        pairing.HideQrCommand.Execute(null);
        Assert.Equal(QrHiddenReason.Button, pairing.HiddenReason);
        Assert.Equal("QR hidden. Existing pairing remains valid.", pairing.Hint);
        Assert.False(pairing.HideQrCommand.CanExecute(null));

        // The hidden QR's time is over: a later tick changes nothing.
        parts.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(QrHiddenReason.Button, pairing.HiddenReason);

        // The relay ends while a QR is shown: the stopped layout.
        parts.ProduceQr();
        parts.SelfCheck(SelfCheckResult.Verified);
        parts.Stop();
        Assert.Equal(QrState.Hidden, pairing.QrState);
        Assert.Equal(QrHiddenReason.RelayEnded, pairing.HiddenReason);
        Assert.Equal(QrTileFace.RelayNotRunning, pairing.TileFace);
        Assert.Equal("Start the relay to show the QR", pairing.PlaceholderLine);
        Assert.Equal("Pair your iPhone once. Restarts and showing this QR keep existing pairings valid.", pairing.Hint);
        Assert.Null(pairing.VerifyNotice);

        // The address of the last start is kept, and can be copied.
        Assert.Equal(PairingParts.Endpoint, pairing.PcAddress);
        pairing.CopyAddressCommand.Execute(null);
        Assert.Equal(PairingParts.Endpoint, parts.Clipboard.Last);
        Assert.Equal(["Copied"], parts.Toasts.Texts);
    }

    [Fact]
    public async Task A_reset_shows_the_new_qr_with_its_own_hint()
    {
        var parts = new PairingParts();
        var pairing = parts.Pairing;
        parts.Run();

        // With the relay running: the matrix arrives while the confirmed reset is still running.
        pairing.ResetPairingCommand.Execute(null);
        var dialog = Assert.IsType<ResetPairingDialogViewModel>(Assert.Single(parts.Dialogs.Shown));
        parts.Session.Gate = new TaskCompletionSource();
        dialog.ConfirmCommand.Execute(null);
        Assert.Equal(QrTileFace.Generating, pairing.TileFace);
        parts.ProduceQr();
        parts.Session.Gate.SetResult();
        parts.Session.Gate = null;
        await Task.Yield();

        Assert.Null(parts.Dialogs.Current);
        Assert.Equal(QrState.Shown, pairing.QrState);
        Assert.Equal("Pairing reset. Scan this QR on each iPhone.", pairing.Hint);
        Assert.True(pairing.CanResetPairing);

        // Showing it again afterwards is an ordinary display.
        pairing.HideQrCommand.Execute(null);
        parts.ProduceQr();
        Assert.Equal(
            "On iPhone: Pair Windows PC > Scan PC QR code, then Verify and pair. QR hides after 2 minutes.",
            pairing.Hint);

        // With the relay stopped the stored pairing is replaced; the new QR is the first one after
        // the next start.
        parts.Stop();
        pairing.ResetPairingCommand.Execute(null);
        var second = Assert.IsType<ResetPairingDialogViewModel>(parts.Dialogs.Shown[^1]);
        second.ConfirmCommand.Execute(null);
        await Task.Yield();
        Assert.Null(parts.Dialogs.Current);
        Assert.Equal(2, parts.Session.ResetPairingCalls);
        Assert.Equal(QrState.Hidden, pairing.QrState);

        parts.Run();
        parts.ProduceQr();
        Assert.Equal("Pairing reset. Scan this QR on each iPhone.", pairing.Hint);
        parts.ProduceQr();
        Assert.NotEqual("Pairing reset. Scan this QR on each iPhone.", pairing.Hint);

        // A cancelled dialog resets nothing and marks nothing.
        pairing.ResetPairingCommand.Execute(null);
        parts.Dialogs.Current!.CancelCommand.Execute(null);
        parts.ProduceQr();
        Assert.NotEqual("Pairing reset. Scan this QR on each iPhone.", pairing.Hint);
        Assert.Equal(2, parts.Session.ResetPairingCalls);
    }

    [Fact]
    public void Hiding_releases_the_matrix()
    {
        // Button.
        var parts = new PairingParts();
        parts.Run();
        var shown = ShowThenHide(parts);
        Assert.Null(parts.Pairing.QrModules);

        // Timer.
        parts.ProduceQr();
        Assert.NotNull(parts.Pairing.QrModules);
        parts.Clock.Advance(QrCountdown.Duration);
        Assert.Null(parts.Pairing.QrModules);
        Assert.Equal(QrHiddenReason.Timer, parts.Pairing.HiddenReason);

        // Relay end.
        parts.ProduceQr();
        Assert.NotNull(parts.Pairing.QrModules);
        parts.Stop();
        Assert.Null(parts.Pairing.QrModules);
        Assert.Equal(QrHiddenReason.RelayEnded, parts.Pairing.HiddenReason);

        // Released, not just covered: nothing in the view model keeps a matrix alive.
        var held = typeof(PairingViewModel)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => field.FieldType == typeof(bool[][]) || field.FieldType == typeof(PairingQr))
            .Select(field => field.GetValue(parts.Pairing))
            .ToList();
        Assert.All(held, Assert.Null);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(shown.IsAlive);

        // The countdown stopped with it: no timer of the page is left running.
        Assert.Equal(0, parts.Clock.ScheduledTimers);
    }

    /// <summary>Shows a matrix and hides it with the button; what is left of the matrix is a weak reference.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ShowThenHide(PairingParts parts)
    {
        var matrix = new WeakReference(parts.ProduceQr());
        Assert.NotNull(parts.Pairing.QrModules);
        parts.Pairing.HideQrCommand.Execute(null);
        return matrix;
    }

    [Fact]
    public void Show_is_gated_by_the_connection()
    {
        var parts = new PairingParts();
        var pairing = parts.Pairing;

        // Relay not running.
        Assert.False(pairing.CanShowQr);
        Assert.False(pairing.ShowQrCommand.CanExecute(null));
        Assert.Equal("Start the relay first.", pairing.ShowQrCaption);
        Assert.Equal(QrTileFace.RelayNotRunning, pairing.TileFace);
        Assert.Equal("Start the relay to show the QR", pairing.PlaceholderLine);

        // Running, but Tailscale forwards nothing to it yet.
        parts.Session.State = RelayUiState.Ready;
        parts.Session.Connection = PairingParts.Inspection(MappingState.Absent);
        Assert.False(pairing.CanShowQr);
        Assert.False(pairing.ShowQrCommand.CanExecute(null));
        Assert.Equal("Set up the connection first.", pairing.ShowQrCaption);
        Assert.Equal(QrTileFace.ConnectionNotSetUp, pairing.TileFace);
        Assert.Equal("Set up the connection first.", pairing.PlaceholderLine);

        foreach (var state in new[] { MappingState.Blocked, MappingState.Unknown })
        {
            parts.Session.Connection = PairingParts.Inspection(state);
            Assert.False(pairing.ShowQrCommand.CanExecute(null));
        }

        // The mapping is there, LiveCue's own or not: the QR can be shown.
        foreach (var state in new[] { MappingState.PresentNotOwned, MappingState.Present })
        {
            parts.Session.Connection = PairingParts.Inspection(state);
            Assert.True(pairing.CanShowQr);
            Assert.True(pairing.ShowQrCommand.CanExecute(null));
            Assert.Null(pairing.ShowQrCaption);
            Assert.Equal(QrTileFace.Placeholder, pairing.TileFace);
        }

        // Paused still runs; stopped does not.
        parts.Session.State = RelayUiState.Paused;
        Assert.True(pairing.ShowQrCommand.CanExecute(null));
        parts.Session.State = RelayUiState.Stopped;
        Assert.False(pairing.ShowQrCommand.CanExecute(null));
        Assert.Equal("Start the relay first.", pairing.ShowQrCaption);

        // Reset is offered running or stopped, and only not while a start is in progress.
        Assert.True(pairing.ResetPairingCommand.CanExecute(null));
        parts.Session.State = RelayUiState.Checking;
        Assert.False(pairing.CanResetPairing);
        parts.Session.State = RelayUiState.Starting;
        Assert.False(pairing.ResetPairingCommand.CanExecute(null));
        parts.Session.State = RelayUiState.Faulted;
        Assert.True(pairing.ResetPairingCommand.CanExecute(null));
        Assert.Equal(0, parts.Session.ShowPairingCalls);
    }

    [Fact]
    public void Verify_notice_cases()
    {
        var parts = new PairingParts();
        var pairing = parts.Pairing;
        parts.Run();

        // Nothing is shown: a result belongs to nothing on screen.
        parts.SelfCheck(SelfCheckResult.Verified);
        Assert.Null(pairing.VerifyNotice);

        parts.ProduceQr();
        Assert.Null(pairing.VerifyNotice);

        parts.SelfCheck(SelfCheckResult.Verified);
        Assert.Equal(new BannerModel(BannerTone.Success, null, "Secure PC connection verified."), pairing.VerifyNotice);

        parts.SelfCheck(SelfCheckResult.VerifiedThroughFunnel);
        Assert.Equal(
            new BannerModel(BannerTone.Success, null, "Secure Funnel connection verified. The iPhone VPN is not required."),
            pairing.VerifyNotice);

        // Not verified: a warning with the way to Settings; the QR stays.
        parts.SelfCheck(SelfCheckResult.NotVerified);
        Assert.Equal(
            new BannerModel(
                BannerTone.Warning,
                null,
                "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before pairing.",
                "Open Settings"),
            pairing.VerifyNotice);
        Assert.Equal(QrState.Shown, pairing.QrState);
        pairing.OpenSettingsCommand.Execute(null);
        Assert.Equal(AppPage.Settings, parts.Navigator.Current);

        // Each display starts without a notice and gets its own.
        parts.ProduceQr();
        Assert.Null(pairing.VerifyNotice);
        parts.SelfCheck(SelfCheckResult.Verified);
        Assert.NotNull(pairing.VerifyNotice);

        // Hidden: the notice leaves, and a result that comes late is not shown.
        pairing.HideQrCommand.Execute(null);
        Assert.Null(pairing.VerifyNotice);
        parts.SelfCheck(SelfCheckResult.NotVerified);
        Assert.Null(pairing.VerifyNotice);

        // A result that overtakes its own matrix is kept for it.
        parts.Session.Gate = new TaskCompletionSource();
        pairing.ShowQrCommand.Execute(null);
        parts.SelfCheck(SelfCheckResult.Verified);
        parts.ProduceQr();
        parts.Session.Gate.SetResult();
        Assert.Equal("Secure PC connection verified.", pairing.VerifyNotice?.Body);
    }

    [Fact]
    public void Navigation_does_not_request_a_qr()
    {
        var parts = new PairingParts();
        parts.Run();

        // Opening the page, leaving it and coming back asks the session for nothing (WIN-20).
        foreach (var page in new[] { AppPage.Pairing, AppPage.Live, AppPage.Pairing, AppPage.Settings, AppPage.Pairing })
        {
            parts.Navigator.Navigate(page);
        }

        Assert.Equal(0, parts.Session.ShowPairingCalls);
        Assert.Equal(QrState.Empty, parts.Pairing.QrState);

        // A shown QR stays shown while another page is open, and its time keeps running.
        var matrix = parts.ProduceQr();
        parts.Navigator.Navigate(AppPage.Live);
        parts.Clock.Advance(TimeSpan.FromSeconds(60));
        parts.Navigator.Navigate(AppPage.Pairing);
        Assert.Same(matrix, parts.Pairing.QrModules);
        Assert.Equal("Hides in 1:00", parts.Pairing.CountdownText);
        Assert.Equal(0, parts.Session.ShowPairingCalls);

        // Only the button asks, once per press.
        parts.Pairing.ShowQrCommand.Execute(null);
        Assert.Equal(1, parts.Session.ShowPairingCalls);
    }

    [Fact]
    public void No_secret_member()
    {
        // The desktop receives the QR's modules and the endpoint, nothing else (decision A-16).
        Assert.Equal(
            ["Endpoint", "Modules"],
            typeof(PairingQr).GetProperties().Select(property => property.Name).Order());

        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        string[] forbidden = ["token", "payload", "secret", "pairingtext", "bearer", "password"];
        foreach (var type in new[]
                 {
                     typeof(IPairingViewModel), typeof(PairingViewModel), typeof(QrCountdown),
                     typeof(IResetPairingDialogViewModel), typeof(ResetPairingDialogViewModel),
                 })
        {
            var names = type.GetMembers(all | BindingFlags.DeclaredOnly).Select(member => member.Name);
            Assert.DoesNotContain(names, name => forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        // What the page can copy is the address; it has no command for anything else.
        var commands = typeof(IPairingViewModel).GetProperties()
            .Where(property => property.PropertyType == typeof(System.Windows.Input.ICommand))
            .Select(property => property.Name)
            .Order();
        Assert.Equal(
            ["CopyAddressCommand", "HideQrCommand", "OpenSettingsCommand", "ResetPairingCommand", "ShowQrCommand"],
            commands);

        // Nothing the page shows as text is, or looks like, a pairing token; the matrix is the only
        // thing that carries it, and only while it is drawn.
        var parts = new PairingParts();
        parts.Run();
        parts.ProduceQr();
        parts.SelfCheck(SelfCheckResult.Verified);
        var texts = typeof(IPairingViewModel).GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => (string?)property.GetValue(parts.Pairing))
            .Append(parts.Pairing.VerifyNotice?.Body)
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();
        Assert.NotEmpty(texts);
        Assert.DoesNotContain(texts, text => System.Text.RegularExpressions.Regex.IsMatch(text!, "[A-Za-z0-9_-]{43}"));
        Assert.DoesNotContain(texts, text => text!.Contains("\"token\"", StringComparison.OrdinalIgnoreCase));

        // Copying copies the address and nothing else.
        parts.Pairing.CopyAddressCommand.Execute(null);
        Assert.Equal([PairingParts.Endpoint], parts.Clipboard.Texts);
    }
}
