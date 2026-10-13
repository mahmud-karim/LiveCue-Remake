using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.Tests.Live;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Pairing;

/// <summary>
/// The Pairing page in the real window, off-screen, over a fake session (screens section 3; W-design
/// 5.6), at the default and at the minimum size. Every QR here is the synthetic fixture matrix. With
/// the environment variable <c>LIVECUE_TEST_SHOTS</c> set, each scene is also written as a PNG for a
/// person to compare with the mockup frames (win-pairing-shown, win-pairing-reset).
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class PairingPageTests
{
    private const string Footnote =
        "Pairing codes are private. Codes hide after 2 minutes. Showing the QR again keeps existing pairings valid.";

    private const string ResetCaption =
        "Creates a new pairing code. Every iPhone paired with this PC stops working until it scans the new QR.";

    [Fact]
    public void The_page_at_rest_says_why_the_qr_cannot_be_shown() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Show(AppPage.Pairing);

        // Relay not running: the placeholder, a disabled Show QR with its caption, no address yet.
        Assert.Single(WpfApp.Descendants(app.Page).OfType<PairingPage>());
        Assert.Equal(
            [
                "Pairing",
                "Your pairing code", "Start the relay to show the QR",
                "Pair your iPhone",
                "Pair your iPhone once. Restarts and showing this QR keep existing pairings valid.",
                "Show QR", "Hide QR", "Start the relay first.",
                "PC address", "Shown when the relay is running",
                Footnote,
                "Reset pairing…", "Reset pairing", ResetCaption,
            ],
            app.Texts());
        Assert.False(app.Shown<Button>(AutomationIds.ShowQrButton).IsEnabled);
        Assert.False(app.Shown<Button>(AutomationIds.HideQrButton).IsEnabled);
        Assert.True(app.Shown<Button>(AutomationIds.ResetPairingButton).IsEnabled);
        Assert.Null(app.Find(AutomationIds.PairingVerifyNotice));

        // The QR is one element for assistive technology: its name and its state, never its content.
        var qr = app.Shown<QrCodeView>(AutomationIds.PairingQr);
        var peer = UIElementAutomationPeer.CreatePeerForElement(qr);
        Assert.Equal("Pairing QR code, hidden", peer.GetName());
        Assert.Equal(AutomationControlType.Image, peer.GetAutomationControlType());
        Assert.Empty(peer.GetChildren() ?? []);
        app.Capture("w10-pairing-stopped", (_, _) => RequireLayout(app, panelWidth: 304));

        // The relay runs, but nothing forwards to it yet.
        app.Run();
        app.Session.Connection = PageParts.Inspection(MappingState.Absent);
        WpfApp.Settle();
        var texts = app.Texts();
        Assert.Contains("Set up the connection first.", texts);
        Assert.DoesNotContain("Start the relay first.", texts);
        Assert.Contains(PageParts.Endpoint, texts);
        Assert.False(app.Shown<Button>(AutomationIds.ShowQrButton).IsEnabled);
        app.Capture("w10-pairing-not-set-up", (_, _) => RequireLayout(app, panelWidth: 304));

        // The connection is set up: Show QR is the page's one accent button, and its caption is gone.
        app.Session.Connection = PageParts.Inspection(MappingState.Present);
        WpfApp.Settle();
        Assert.True(app.Shown<Button>(AutomationIds.ShowQrButton).IsEnabled);
        Assert.Contains("Show the QR to pair an iPhone", app.Texts());
        Assert.DoesNotContain("Set up the connection first.", app.Texts());

        // Asking: the tile turns while the session works.
        app.Session.Gate = new TaskCompletionSource();
        app.Shown<Button>(AutomationIds.ShowQrButton).Command.Execute(null);
        WpfApp.Settle();
        Assert.Equal(1, app.Session.ShowPairingCalls);
        Assert.True(ButtonAssist.GetIsBusy(app.Shown<Button>(AutomationIds.ShowQrButton)));
        Assert.Contains(WpfApp.Descendants(Named(app, "QrTile")).OfType<Spinner>(), spinner => spinner.IsVisible);
        Assert.DoesNotContain("Your pairing code", app.Texts());
        app.Capture("w10-pairing-generating", (_, _) => RequireLayout(app, panelWidth: 304));
        app.Session.Gate.SetResult();
        app.Session.Gate = null;
        WpfApp.Settle();
        Assert.False(ButtonAssist.GetIsBusy(app.Shown<Button>(AutomationIds.ShowQrButton)));
        Assert.DoesNotContain(WpfApp.Descendants(Named(app, "QrTile")).OfType<Spinner>(), spinner => spinner.IsVisible);
    });

    [Fact]
    public void A_shown_qr_is_drawn_on_whole_pixels_with_its_quiet_zone_and_counts_down() => WpfApp.Run(() =>
    {
        using var app = new PageWindow();
        app.Run();
        app.Show(AppPage.Live);

        // The matrix arrives: the window goes to Pairing by itself.
        const int modules = 45;
        var matrix = QrFixture.Matrix(modules);
        app.Session.RaisePairingQr(new PairingQr(PageParts.Endpoint, matrix));
        app.Session.RaiseRelayEvent(new SelfCheckCompleted(app.Now, SelfCheckResult.Verified));
        WpfApp.Settle(TimeSpan.FromMilliseconds(260));
        Assert.Equal(AppPage.Pairing, app.Shell.SelectedPage);

        Assert.Equal(
            [
                "Pairing",
                "Hides in 2:00",
                "Pair your iPhone",
                "On iPhone: Pair Windows PC > Scan PC QR code, then Verify and pair. QR hides after 2 minutes.",
                "Show QR", "Hide QR",
                "PC address", PageParts.Endpoint,
                "Secure PC connection verified.",
                Footnote,
                "Reset pairing…", "Reset pairing", ResetCaption,
            ],
            app.Texts());
        Assert.True(app.Shown<Button>(AutomationIds.HideQrButton).IsEnabled);
        var notice = app.Shown<Banner>(AutomationIds.PairingVerifyNotice);
        Assert.Equal(BannerTone.Success, notice.Tone);
        Assert.Null(notice.ActionLabel);

        var qr = app.Shown<QrCodeView>(AutomationIds.PairingQr);
        Assert.Same(matrix, qr.Modules);
        Assert.Equal("Pairing QR code, shown, hides in 2 minutes", UIElementAutomationPeer.CreatePeerForElement(qr).GetName());

        // 45 modules and the quiet zone are 53; at 96 DPI a module is 4 pixels and the square 212.
        var module = QrGeometry.ModulePixels(modules, 1);
        Assert.Equal(4, module);
        Assert.Equal(new Size(212, 212), qr.RenderSize);
        var dark = ((SolidColorBrush)app.Window.FindResource("QrModuleBrush")).Color;
        var white = ((SolidColorBrush)app.Window.FindResource("SurfaceQrBrush")).Color;
        Assert.Equal(Colors.White, white);

        app.Capture("w10-pairing-shown", (_, _) =>
        {
            RequireLayout(app, panelWidth: 304);
            var box = app.Bounds(qr);
            Assert.Equal(Math.Round(box.Left), box.Left, 6);
            Assert.Equal(Math.Round(box.Top), box.Top, 6);

            // Every module of the matrix is one flat square of 4 x 4 pixels: no pixel is blended, at
            // the edges of a module as little as in its middle. The quiet zone is white all round.
            var picture = WpfApp.Render(app.Root);
            var left = (int)box.Left;
            var top = (int)box.Top;
            for (var row = -QrGeometry.QuietZoneModules; row < modules + QrGeometry.QuietZoneModules; row++)
            {
                for (var column = -QrGeometry.QuietZoneModules; column < modules + QrGeometry.QuietZoneModules; column++)
                {
                    var inside = row >= 0 && row < modules && column >= 0 && column < modules;
                    var expected = inside && matrix[row][column] ? dark : white;
                    var x = left + ((column + QrGeometry.QuietZoneModules) * module);
                    var y = top + ((row + QrGeometry.QuietZoneModules) * module);
                    Assert.Equal(expected, WpfApp.PixelAt(picture, x, y));
                    Assert.Equal(expected, WpfApp.PixelAt(picture, x + module - 1, y + module - 1));
                }
            }
        });

        // The last 30 seconds: the chip warns. A check that failed keeps the QR and offers Settings.
        app.Clock.Advance(TimeSpan.FromSeconds(93));
        app.Session.RaiseRelayEvent(new SelfCheckCompleted(app.Now, SelfCheckResult.NotVerified));
        WpfApp.Settle();
        Assert.Contains("Hides in 0:27", app.Texts());
        Assert.True(app.Shell.Pairing.IsCountdownWarning);
        Assert.Equal(BannerTone.Warning, notice.Tone);
        Assert.Equal("Open Settings", notice.ActionLabel);
        Assert.Equal(
            "Pairing code created, but the advertised connection could not be verified. Check the PC tunnel before pairing.",
            notice.Body);
        app.Capture("w10-pairing-expiring", (_, _) => RequireLayout(app, panelWidth: 304));

        // A larger code needs more room than 240: the tile and its panel grow with it, never the reverse.
        app.Session.RaisePairingQr(new PairingQr(PageParts.Endpoint, QrFixture.Matrix(57)));
        WpfApp.Settle();
        Assert.Equal(new Size(260, 260), qr.RenderSize);
        app.Capture("w10-pairing-shown-large-code", (_, _) => RequireLayout(app, panelWidth: 324));

        // Time is up: the matrix is gone from the view, and the tile says how to get it back.
        app.Clock.Advance(TimeSpan.FromSeconds(120));
        WpfApp.Settle();
        Assert.Null(qr.Modules);
        Assert.Equal(default, qr.RenderSize);
        var texts = app.Texts();
        Assert.Contains("QR hidden for privacy. Existing pairing remains valid.", texts);
        Assert.Contains("Show the QR to pair an iPhone", texts);
        Assert.DoesNotContain(texts, text => text.StartsWith("Hides in", StringComparison.Ordinal));
        Assert.Null(app.Find(AutomationIds.PairingVerifyNotice));
        Assert.False(app.Shown<Button>(AutomationIds.HideQrButton).IsEnabled);
        Assert.Equal("Pairing QR code, hidden", UIElementAutomationPeer.CreatePeerForElement(qr).GetName());
        app.Capture("w10-pairing-hidden", (_, _) => RequireLayout(app, panelWidth: 304));
    });

    [Fact]
    public void Public_funnel_has_its_own_verification_text() => WpfApp.Run(() =>
    {
        // A public address as long as a real one: the PC's name, the tailnet's name, the port and the path.
        const string endpoint = "https://livecue-desktop.tailnet-example.ts.net:10000/livecue/";
        using var app = new PageWindow();
        app.Run();
        app.Session.AdvertisedEndpoint = endpoint;
        app.Session.RaisePairingQr(new PairingQr(endpoint, QrFixture.Matrix()));
        app.Session.RaiseRelayEvent(new SelfCheckCompleted(app.Now, SelfCheckResult.VerifiedThroughFunnel));
        WpfApp.Settle(TimeSpan.FromMilliseconds(260));

        var notice = app.Shown<Banner>(AutomationIds.PairingVerifyNotice);
        Assert.Equal(BannerTone.Success, notice.Tone);
        Assert.Equal("Secure Funnel connection verified. The iPhone VPN is not required.", notice.Body);

        // 41 modules need 5 pixels each to reach 200: the square is 245 and the panel follows it.
        Assert.Equal(new Size(245, 245), app.Shown<QrCodeView>(AutomationIds.PairingQr).RenderSize);
        var address = WpfApp.Descendants(app.Page).OfType<TextBlock>().Single(text => text.Text == endpoint);
        var field = app.Shown<ContentControl>(AutomationIds.PairingAddressField);
        app.Capture("w10-pairing-shown-funnel", (width, _) =>
        {
            RequireLayout(app, panelWidth: 309);

            // The address is never cut off: where it does not fit on one line it wraps, and the
            // field, 44 high for one line, grows with it.
            Assert.Equal(TextTrimming.None, address.TextTrimming);
            Assert.Equal(TextWrapping.Wrap, address.TextWrapping);
            if (width >= 1100)
            {
                Assert.Equal(18, address.ActualHeight, 0.5);
                Assert.Equal(44, field.ActualHeight, 0.5);
            }
            else
            {
                Assert.True(address.ActualHeight >= 36);
                Assert.Equal(0, address.ActualHeight % 18, 6);
                Assert.Equal(Math.Max(44, address.ActualHeight + 8), field.ActualHeight, 0.5);
            }

            Assert.True(app.Bounds(address).Right <= app.Bounds(field).Right - 14);
            Assert.True(app.Bounds(address).Top >= app.Bounds(field).Top + 4 - 0.5);
        });

        // Hide QR: the page says the pairing is still good.
        app.Shown<Button>(AutomationIds.HideQrButton).Command.Execute(null);
        WpfApp.Settle();
        Assert.Contains("QR hidden. Existing pairing remains valid.", app.Texts());
        Assert.Null(app.Find(AutomationIds.PairingVerifyNotice));
    });

    /// <summary>
    /// The frame of the page (screens 3.1 and section 7): the QR panel at the left, as wide as its tile
    /// needs; the other panel takes the rest and is as high; the reset panel is by itself under both,
    /// as wide as the page; the two buttons share a line; the tile is square and opaque white.
    /// </summary>
    private static void RequireLayout(PageWindow app, double panelWidth)
    {
        var page = app.Bounds(app.Page);
        var qrPanel = app.Bounds(Named(app, "QrPanel"));
        var pairPanel = app.Bounds(Named(app, "PairPanel"));
        var resetPanel = app.Bounds(Named(app, "ResetPanel"));
        var tile = app.Bounds(Named(app, "QrTile"));

        Assert.Equal(page.Left, qrPanel.Left, 0.5);
        Assert.Equal(panelWidth, qrPanel.Width, 0.5);
        Assert.Equal(qrPanel.Right + 16, pairPanel.Left, 0.5);
        Assert.Equal(page.Right, pairPanel.Right, 0.5);
        Assert.Equal(qrPanel.Top, pairPanel.Top, 0.5);
        Assert.Equal(qrPanel.Height, pairPanel.Height, 0.5);

        Assert.Equal(qrPanel.Bottom + 16, resetPanel.Top, 0.5);
        Assert.Equal(page.Left, resetPanel.Left, 0.5);
        Assert.Equal(page.Right, resetPanel.Right, 0.5);

        // The tile: 12 of padding around a code area of at least 240, 20 inside the panel.
        Assert.Equal(tile.Width, tile.Height, 0.5);
        Assert.True(tile.Width >= 264);
        Assert.Equal(panelWidth - 40, tile.Width, 0.5);
        Assert.Equal(qrPanel.Left + 20, tile.Left, 0.5);
        Assert.Equal(qrPanel.Top + 20, tile.Top, 0.5);

        var show = app.Bounds(app.Shown<Button>(AutomationIds.ShowQrButton));
        var hide = app.Bounds(app.Shown<Button>(AutomationIds.HideQrButton));
        Assert.Equal(show.Top, hide.Top, 0.5);
        Assert.Equal(show.Right + 8, hide.Left, 0.5);
        Assert.Equal(40, show.Height, 0.5);

        // Nothing is cut off at the right, and the reset button stays inside its panel.
        var reset = app.Bounds(app.Shown<Button>(AutomationIds.ResetPairingButton));
        Assert.True(reset.Right <= resetPanel.Right - 20 + 0.5);
        Assert.True(reset.Top >= resetPanel.Top && reset.Bottom <= resetPanel.Bottom);
    }

    private static FrameworkElement Named(PageWindow app, string name) =>
        WpfApp.Descendants(app.Page).OfType<FrameworkElement>().First(element => element.Name == name);
}
