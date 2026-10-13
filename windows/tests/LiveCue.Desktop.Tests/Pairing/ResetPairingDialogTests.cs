using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Tests.Live;
using LiveCue.Desktop.Tests.Support;

namespace LiveCue.Desktop.Tests.Pairing;

/// <summary>
/// The reset confirmation in the real window (test plan 2.8; screens 6.1; W-design 5.9; mockup
/// win-pairing-reset): the only destructive action of the app asks first, Cancel has the focus, Enter
/// does not confirm, and a reset that failed stays open with its text.
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class ResetPairingDialogTests
{
    private const string BodyRunning =
        "This replaces the pairing code. Every iPhone paired with this PC must scan the new QR and pair again. The relay keeps running.";

    private const string BodyStopped =
        "This replaces the pairing code. Every iPhone paired with this PC must scan the new QR and pair again.";

    private const string Failed =
        "The pairing could not be reset. Check that LiveCue can write its data folder, then retry.";

    [Fact]
    public void Behaviour() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Run();

        // Behind the dialog: the page after the two-minute timer hid the QR, as in the mockup.
        app.Session.RaisePairingQr(new LiveCue.Relay.PairingQr(PageParts.Endpoint, QrFixture.Matrix()));
        app.Clock.Advance(QrCountdown.Duration);
        WpfApp.Settle(TimeSpan.FromMilliseconds(260));
        var card = (ContentControl)app.Window.FindName("DialogCard");

        var opener = app.Shown<Button>(AutomationIds.ResetPairingButton);
        FocusManager.SetFocusedElement(app.Window, opener);
        opener.Command.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));

        // The texts of screens 6.1, with the sentence about the running relay.
        var dialog = Assert.IsType<ResetPairingDialogViewModel>(app.Shell.ActiveDialog);
        var view = Assert.Single(WpfApp.Descendants(card).OfType<ResetPairingDialog>());
        Assert.Equal(AutomationIds.ResetPairingDialog, AutomationProperties.GetAutomationId(view));
        Assert.Equal(["Reset pairing?", BodyRunning, "Cancel", "Reset pairing"], Texts(card));
        Assert.DoesNotContain(WpfApp.Descendants(card).OfType<Banner>(), banner => banner.IsVisible);

        // Cancel has the focus. Nothing in the dialog is a default button, so Enter confirms nothing:
        // on the focused Cancel it cancels.
        var buttons = WpfApp.Descendants(card).OfType<Button>().Where(button => button.IsVisible).ToList();
        var cancel = Assert.Single(buttons, button => button.IsCancel);
        var confirm = Assert.Single(buttons, button => AutomationProperties.GetAutomationId(button) == AutomationIds.ConfirmResetPairingButton);
        var close = Assert.Single(buttons, button => AutomationProperties.GetName(button) == "Close");
        Assert.Equal(3, buttons.Count);
        Assert.Equal("Cancel", cancel.Content);
        Assert.Equal("Reset pairing", confirm.Content);
        Assert.Same(cancel, FocusManager.GetFocusedElement(app.Window));
        Assert.DoesNotContain(buttons, button => button.IsDefault);
        Assert.Same(app.Window.FindResource("Button.DestructivePrimary"), confirm.Style);
        Assert.Same(app.Window.FindResource("Button.Secondary"), cancel.Style);

        // The card: the close button at the title's right, the two buttons at the bottom right, 8 apart.
        var cardBox = app.Bounds(card);
        Assert.Equal(480, cardBox.Width);
        Assert.Equal(app.Bounds(confirm).Right, cardBox.Right - 24, 0.5);
        Assert.Equal(app.Bounds(cancel).Right + 8, app.Bounds(confirm).Left, 0.5);
        Assert.Equal(app.Bounds(confirm).Bottom, cardBox.Bottom - 24, 0.5);
        Assert.True(app.Bounds(close).Right <= cardBox.Right - 12);
        Assert.True(app.Bounds(close).Top >= cardBox.Top + 12);
        Assert.Equal(cardBox.Left + 24, app.Bounds(Text(card, "Reset pairing?")).Left, 0.5);
        Assert.Equal(cardBox.Top + 24, app.Bounds(Text(card, "Reset pairing?")).Top, 0.5);
        Assert.True(VisualChecks.RequireCrispText(app.Root) > 0);
        VisualChecks.RequireEffectsOnlyOnCasters(app.Root);
        WpfApp.Render(app.Root, "w10-pairing-reset-1240x660.png");

        PressEnter(cancel);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Null(app.Shell.ActiveDialog);
        Assert.Equal(0, app.Session.ResetPairingCalls);
        Assert.Same(opener, FocusManager.GetFocusedElement(app.Window));

        // The close button cancels too.
        opener.Command.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        WpfApp.Descendants(card).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Close").Command.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Null(app.Shell.ActiveDialog);
        Assert.Equal(0, app.Session.ResetPairingCalls);

        // A reset that fails stays open with its text, and can be tried again. While it runs the
        // dialog is busy: it cannot be cancelled or confirmed a second time.
        app.Session.ResetSucceeds = false;
        app.Session.Gate = new TaskCompletionSource();
        opener.Command.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        dialog = Assert.IsType<ResetPairingDialogViewModel>(app.Shell.ActiveDialog);
        confirm = WpfApp.Descendants(card).OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == AutomationIds.ConfirmResetPairingButton);
        confirm.Command.Execute(null);
        WpfApp.Settle();
        Assert.True(dialog.IsBusy);
        Assert.True(ButtonAssist.GetIsBusy(confirm));
        Assert.False(dialog.CancelCommand.CanExecute(null));
        Assert.False(dialog.ConfirmCommand.CanExecute(null));
        app.Shell.EscapeCommand.Execute(null);
        Assert.Same(dialog, app.Shell.ActiveDialog);

        app.Session.Gate.SetResult();
        app.Session.Gate = null;
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Same(dialog, app.Shell.ActiveDialog);
        Assert.False(dialog.IsBusy);
        var failure = Assert.Single(WpfApp.Descendants(card).OfType<Banner>(), banner => banner.IsVisible);
        Assert.Equal(BannerTone.Danger, failure.Tone);
        Assert.Equal(Failed, failure.Body);
        Assert.Equal(new BannerModel(BannerTone.Danger, null, Failed), dialog.Failure);
        Assert.Equal(1, app.Session.ResetPairingCalls);
        WpfApp.Render(app.Root, "w10-pairing-reset-failed-1240x660.png");

        // Retried, it works: the failure leaves, the dialog closes, the new QR is on the page.
        app.Session.ResetSucceeds = true;
        confirm.Command.Execute(null);
        app.Session.RaisePairingQr(new LiveCue.Relay.PairingQr(PageParts.Endpoint, QrFixture.Matrix()));
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Null(app.Shell.ActiveDialog);
        Assert.Equal(2, app.Session.ResetPairingCalls);

        // With the relay stopped the last sentence is left out.
        app.Session.State = RelayUiState.Stopped;
        app.Session.RaiseSignal(SessionSignal.RelayEnded);
        opener.Command.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Equal(BodyStopped, Assert.IsType<ResetPairingDialogViewModel>(app.Shell.ActiveDialog).Body);
        Assert.Equal(["Reset pairing?", BodyStopped, "Cancel", "Reset pairing"], Texts(card));
        app.Shell.EscapeCommand.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(60));
        Assert.Null(app.Shell.ActiveDialog);
    });

    /// <summary>The Enter key, as the focused element receives it.</summary>
    private static void PressEnter(UIElement focused)
    {
        var source = PresentationSource.FromVisual(focused) ?? throw new InvalidOperationException("The element is in no window.");
        focused.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        focused.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyUpEvent });
    }

    private static List<string> Texts(DependencyObject root) => WpfApp.Descendants(root)
        .OfType<TextBlock>()
        .Where(text => text.IsVisible && !string.IsNullOrEmpty(text.Text))
        .Select(text => text.Text)
        .ToList();

    private static TextBlock Text(DependencyObject root, string text) =>
        WpfApp.Descendants(root).OfType<TextBlock>().First(block => block.IsVisible && block.Text == text);
}
