using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Tests.Support;

namespace LiveCue.Desktop.Tests.Shell;

/// <summary>
/// The real window, off-screen, on the real shell (W-design 5.1, 5.2 and 5.9): its chrome, its
/// navigation, and the dialog and toast layers. With the environment variable
/// <c>LIVECUE_TEST_SHOTS</c> set, each scene is also written as a PNG for a person to compare with the
/// mockup.
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class MainWindowTests
{
    [Fact]
    public void The_window_has_the_chrome_of_the_design() => WpfApp.Run(() =>
    {
        using var app = new ShellWindow();
        var window = app.Window;

        // One layout unit is one pixel in this window, so the positions below are exact.
        Assert.Equal(1d, VisualTreeHelper.GetDpi(window).DpiScaleX);

        // W-design 5.1: size, title, an opaque window that paints its own ground.
        Assert.Equal("LiveCue", window.Title);
        Assert.Equal((1240d, 660d), (window.Width, window.Height));
        Assert.Equal((960d, 620d), (window.MinWidth, window.MinHeight));
        Assert.False(window.AllowsTransparency);
        Assert.NotNull(window.Icon);
        Assert.True(window.UseLayoutRounding);
        Assert.True(window.SnapsToDevicePixels);
        Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(window));

        // Design system 3.5: the values of the window chrome. The caption is as high as the title bar.
        var chrome = WindowChrome.GetWindowChrome(window);
        Assert.Equal(48d, (double)window.FindResource("SizeTitleBar"));
        Assert.Equal(48d, chrome.CaptionHeight);
        Assert.Equal(new Thickness(6), chrome.ResizeBorderThickness);
        Assert.Equal(new Thickness(1), chrome.GlassFrameThickness);
        Assert.Equal(new CornerRadius(0), chrome.CornerRadius);
        Assert.False(chrome.UseAeroCaptionButtons);
        Assert.Equal(48d, app.Named<Grid>("TitleBar").ActualHeight);

        // Layers, bottom to top: the ground, the frame with title bar and layout, the dialog layer.
        var layers = app.Named<Grid>("Root").Children;
        Assert.IsType<BloomLayer>(layers[0]);
        Assert.Same(app.Named<Grid>("Frame"), layers[1]);
        Assert.Same(app.Named<Grid>("DialogLayer"), layers[2]);
        Assert.Equal(Visibility.Collapsed, app.Named<Grid>("DialogLayer").Visibility);

        // The title bar: the brand mark and the wordmark on the left, three caption buttons on the
        // right, and nothing else. The buttons have names, can be clicked, and are not tab stops.
        var titleBar = app.Named<Grid>("TitleBar");
        Assert.Single(WpfApp.Descendants(titleBar).OfType<BrandMark>());
        Assert.Equal(["LiveCue"], WpfApp.Descendants(titleBar).OfType<TextBlock>().Select(text => text.Text));
        var captionButtons = WpfApp.Descendants(titleBar).OfType<Button>().ToList();
        Assert.Equal(
            ["Minimize", "Maximize", "Restore", "Close"],
            captionButtons.Select(AutomationProperties.GetName));
        Assert.All(captionButtons, button =>
        {
            Assert.False(button.IsTabStop);
            Assert.False(button.Focusable);
            Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(button));
        });
        Assert.Equal(
            [Visibility.Visible, Visibility.Visible, Visibility.Collapsed, Visibility.Visible],
            captionButtons.Select(button => button.Visibility));

        // An inactive window draws its wordmark and its caption glyphs in the secondary ink.
        var wordmark = WpfApp.Descendants(titleBar).OfType<TextBlock>().Single();
        Assert.Same(window.FindResource("InkPrimaryBrush"), wordmark.Foreground);
        Assert.All(captionButtons, button => Assert.Same(window.FindResource("InkPrimaryBrush"), button.Foreground));
        window.KeepActiveAppearance = false;
        Assert.False(window.IsActive);
        Assert.Same(window.FindResource("InkSecondaryBrush"), wordmark.Foreground);
        Assert.All(captionButtons, button => Assert.Same(window.FindResource("InkSecondaryBrush"), button.Foreground));
        window.KeepActiveAppearance = true;

        // The sidebar: 232 wide, 16 from the window's edge and from the page, its status card at the foot.
        var sidebar = app.Named<ContentControl>("Sidebar");
        var page = app.Named<ContentControl>("PageHost");
        var sidebarBox = Bounds(sidebar, app.Root);
        var pageBox = Bounds(page, app.Root);
        Assert.Equal(232d, sidebar.ActualWidth);
        Assert.Equal(new Rect(16, 48, 232, 660 - 48 - 16), sidebarBox);
        Assert.Equal(new Rect(16 + 232 + 16, 48, 1240 - 264 - 16, 660 - 48 - 16), pageBox);
        var card = app.Named<ContentControl>("StatusCard");
        Assert.Equal(sidebarBox.Bottom - 10, Bounds(card, app.Root).Bottom);
        Assert.Equal(
            ["Relay", "Stopped", "iPhone", "iPhone not connected", "Closing this window stops the relay."],
            WpfApp.Descendants(card).OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text));

        // The same holds at the minimum size.
        window.Width = 960;
        window.Height = 620;
        WpfApp.Settle();
        Assert.Equal(new Rect(16, 48, 232, 620 - 48 - 16), Bounds(sidebar, app.Root));
        Assert.Equal(new Rect(264, 48, 960 - 264 - 16, 620 - 48 - 16), Bounds(page, app.Root));

        // Every text is drawn crisp, and shadows sit on text-free caster layers only.
        Assert.True(VisualChecks.RequireCrispText(app.Root) > 0);
        Assert.True(VisualChecks.RequireEffectsOnlyOnCasters(app.Root) > 0);
    });

    [Fact]
    public void The_sidebar_and_the_keys_choose_the_view() => WpfApp.Run(() =>
    {
        using var app = new ShellWindow();
        var shell = app.Shell;
        var navigation = app.Named<ListBox>("Navigation");
        var page = app.Named<ContentControl>("PageHost");

        // Four rows with the ids of screens section 9; one is always selected.
        var rows = navigation.Items.Cast<ListBoxItem>().ToList();
        Assert.Equal(
            [AutomationIds.NavLive, AutomationIds.NavPairing, AutomationIds.NavActivity, AutomationIds.NavSettings],
            rows.Select(AutomationProperties.GetAutomationId));
        Assert.Equal(["Live", "Pairing", "Activity", "Settings"], rows.Select(AutomationProperties.GetName));
        Assert.Equal(0, navigation.SelectedIndex);
        Assert.Same(shell.Live, page.Content);
        Assert.All(rows, row => Assert.Equal(44d, row.ActualHeight));

        // The list and the shell follow each other, and the page host shows the selected view model.
        navigation.SelectedIndex = 2;
        WpfApp.Settle();
        Assert.Equal(AppPage.Activity, shell.SelectedPage);
        Assert.Same(shell.Activity, page.Content);
        Assert.Single(WpfApp.Descendants(page).OfType<ActivityPage>());

        shell.Navigate(AppPage.Settings);
        WpfApp.Settle();
        Assert.Equal(3, navigation.SelectedIndex);
        Assert.Single(WpfApp.Descendants(page).OfType<SettingsPage>());

        // A list that loses its selection gets it back: one row is always selected.
        navigation.SelectedIndex = -1;
        WpfApp.Settle();
        Assert.Equal(AppPage.Settings, shell.SelectedPage);
        Assert.Equal(3, navigation.SelectedIndex);

        // Ctrl+1 to Ctrl+4 (also on the number pad) run NavigateCommand with a page; Escape runs EscapeCommand.
        var keys = app.Window.InputBindings.OfType<KeyBinding>().ToList();
        (Key Key, AppPage Page)[] expected =
        [
            (Key.D1, AppPage.Live), (Key.D2, AppPage.Pairing), (Key.D3, AppPage.Activity), (Key.D4, AppPage.Settings),
            (Key.NumPad1, AppPage.Live), (Key.NumPad2, AppPage.Pairing), (Key.NumPad3, AppPage.Activity), (Key.NumPad4, AppPage.Settings),
        ];
        foreach (var (key, target) in expected)
        {
            var binding = Assert.Single(keys, candidate => candidate.Key == key);
            Assert.Equal(ModifierKeys.Control, binding.Modifiers);
            Assert.Same(shell.NavigateCommand, binding.Command);
            Assert.Equal(target, binding.CommandParameter);

            binding.Command.Execute(binding.CommandParameter);
            Assert.Equal(target, shell.SelectedPage);
        }

        var escape = Assert.Single(keys, candidate => candidate.Key == Key.Escape);
        Assert.Equal(ModifierKeys.None, escape.Modifiers);
        Assert.Same(shell.EscapeCommand, escape.Command);
        Assert.Equal(9, keys.Count);
    });

    [Fact]
    public void A_dialog_covers_the_window_takes_the_focus_and_gives_it_back() => WpfApp.Run(() =>
    {
        using var app = new ShellWindow();
        var window = app.Window;
        var shell = app.Shell;
        var layer = app.Named<Grid>("DialogLayer");
        var card = app.Named<ContentControl>("DialogCard");
        var scrim = app.Named<Border>("Scrim");

        // The Pairing view, with the focus on the button that opens the dialog.
        shell.Navigate(AppPage.Pairing);
        WpfApp.Settle();
        var opener = WpfApp.Descendants(app.Root).OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == AutomationIds.ResetPairingButton);
        FocusManager.SetFocusedElement(window, opener);

        opener.Command.Execute(opener.CommandParameter);
        WpfApp.Settle(TimeSpan.FromMilliseconds(50));

        // The scrim covers the whole window, the sidebar and the title bar included; the card is 480
        // wide and in the middle.
        var dialog = Assert.IsAssignableFrom<IResetPairingDialogViewModel>(shell.ActiveDialog);
        Assert.Equal(Visibility.Visible, layer.Visibility);
        Assert.Same(dialog, card.Content);
        Assert.Equal(new Rect(0, 0, 1240, 660), Bounds(scrim, app.Root));
        var cardBox = Bounds(card, app.Root);
        Assert.Equal(480d, cardBox.Width);
        Assert.Equal(620d, cardBox.Left + (cardBox.Width / 2));
        Assert.InRange(cardBox.Top + (cardBox.Height / 2), 329, 331);
        Assert.Equal(1d, card.Opacity);
        Assert.Equal(Transform.Identity, card.RenderTransform);
        Assert.Contains("Reset pairing?", WpfApp.Descendants(card).OfType<TextBlock>().Select(text => text.Text));

        // Nothing failed yet, so the dialog's failure banner takes no room.
        Assert.DoesNotContain(WpfApp.Descendants(card).OfType<Banner>(), banner => banner.IsVisible);

        // The focus is inside the dialog, on Cancel: the least destructive choice. It cannot leave by
        // Tab, and the keys and the sidebar do not reach the window behind.
        var focused = Assert.IsType<Button>(FocusManager.GetFocusedElement(window));
        Assert.True(focused.IsDescendantOf(card));
        Assert.True(focused.IsCancel);
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(card));
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(card));
        Assert.False(shell.NavigateCommand.CanExecute(AppPage.Live));

        // The destructive button is not the default one: Enter does not press it.
        var confirm = WpfApp.Descendants(card).OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == AutomationIds.ConfirmResetPairingButton);
        Assert.False(confirm.IsDefault);

        Assert.True(VisualChecks.RequireCrispText(app.Root) > 0);
        VisualChecks.RequireEffectsOnlyOnCasters(app.Root);
        WpfApp.Render(app.Root, "shell-dialog-reset-1240x660.png");

        // Escape cancels: the layer goes, and the focus returns to the button that opened the dialog.
        shell.EscapeCommand.Execute(null);
        WpfApp.Settle(TimeSpan.FromMilliseconds(50));
        Assert.Null(shell.ActiveDialog);
        Assert.Equal(Visibility.Collapsed, layer.Visibility);
        Assert.Null(card.Content);
        Assert.Same(opener, FocusManager.GetFocusedElement(window));
        Assert.True(shell.NavigateCommand.CanExecute(AppPage.Live));
    });

    [Fact]
    public void A_toast_sits_at_the_bottom_centre_of_the_content_area() => WpfApp.Run(() =>
    {
        using var app = new ShellWindow();
        var toast = app.Named<ContentControl>("Toast");
        var label = app.Named<TextBlock>("ToastLabel");
        var page = app.Named<ContentControl>("PageHost");
        Assert.Equal(Visibility.Collapsed, toast.Visibility);

        app.Services.Toasts.Show("Copied");
        WpfApp.Settle(TimeSpan.FromMilliseconds(50));

        Assert.Equal(Visibility.Visible, toast.Visibility);
        Assert.Equal("Copied", label.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(label));
        var toastBox = Bounds(toast, app.Root);
        var pageBox = Bounds(page, app.Root);
        Assert.InRange(toastBox.Left + (toastBox.Width / 2), pageBox.Left + (pageBox.Width / 2) - 1, pageBox.Left + (pageBox.Width / 2) + 1);
        Assert.Equal(pageBox.Bottom - 24, toastBox.Bottom);
        Assert.True(toastBox.Height >= 48);
        Assert.Equal(1d, toast.Opacity);

        Assert.True(VisualChecks.RequireCrispText(app.Root) > 0);
        VisualChecks.RequireEffectsOnlyOnCasters(app.Root);
        WpfApp.Render(app.Root, "shell-toast-1240x660.png");

        // It leaves by itself after four seconds.
        app.Clock.Advance(TimeSpan.FromSeconds(4));
        WpfApp.Settle(TimeSpan.FromMilliseconds(50));
        Assert.Equal(Visibility.Collapsed, toast.Visibility);
        Assert.Null(app.Shell.ToastText);
    });

    [Fact]
    public void The_sidebar_card_shows_the_relay_and_the_phone_as_the_session_has_them() => WpfApp.Run(() =>
    {
        using var app = new ShellWindow();
        var card = app.Named<ContentControl>("StatusCard");
        var indicators = WpfApp.Descendants(card).OfType<StatusIndicatorView>().ToList();
        Assert.Equal(
            [AutomationIds.SidebarRelayStatus, AutomationIds.SidebarPhoneStatus],
            indicators.Select(AutomationProperties.GetAutomationId));

        // A start, then a phone: the card follows without anyone looking at the Live view.
        app.Shell.Navigate(AppPage.Activity);
        _ = app.Session.StartAsync();
        ShellWindow.Until(() => app.Session.State == RelayUiState.Ready, "a running relay");
        app.Script.PhoneSeen();
        ShellWindow.Until(() => app.Session.Presence == PhonePresence.Connected, "a connected phone");

        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Success, "Ready"), indicators[0].Indicator);
        Assert.Equal("iPhone connected", indicators[1].Indicator?.Text);
        Assert.Equal("Ready", indicators[0].Text);
        Assert.Equal("iPhone connected", indicators[1].Text);

        _ = app.Session.StopAsync();
        ShellWindow.Until(() => app.Session.State == RelayUiState.Stopped, "a stopped relay");
        Assert.Equal("Stopped", indicators[0].Text);
        Assert.Equal("iPhone not connected", indicators[1].Text);
    });

    private static Rect Bounds(FrameworkElement element, FrameworkElement root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
}
