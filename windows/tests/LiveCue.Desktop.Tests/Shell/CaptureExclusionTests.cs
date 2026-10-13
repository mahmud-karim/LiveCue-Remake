using System.Windows.Interop;
using LiveCue.Desktop.Interop;
using LiveCue.Desktop.Tests.Support;

namespace LiveCue.Desktop.Tests.Shell;

/// <summary>
/// ST-06 (security review SEC-10): the saved "hide from screen capture" setting reaches the window
/// (W-design 5.12). The first half asks Windows itself what the window's display affinity is; the second
/// plays a Windows that refuses the call.
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class CaptureExclusionTests
{
    private const string Unavailable = "This version of Windows cannot hide the window from screen capture.";

    [Fact]
    public void The_window_follows_the_saved_setting() => WpfApp.Run(() =>
    {
        using (var app = new ShellWindow(show: false))
        {
            var session = app.Session;
            var warnings = new List<string>();
            var asked = new List<uint>();
            Assert.True(session.Settings.HideFromScreenCapture);

            // The real call, with a note of what was asked. Before the window has a handle: nothing.
            app.Window.FollowCaptureSetting(
                session,
                (kind, text) => warnings.Add(kind + ": " + text),
                (handle, affinity) =>
                {
                    asked.Add(affinity);
                    return NativeMethods.SetWindowDisplayAffinity(handle, affinity);
                });
            Assert.Empty(asked);

            // The handle exists: the saved setting is applied.
            var window = new WindowInteropHelper(app.Window).EnsureHandle();
            Assert.Equal([NativeMethods.WdaExcludeFromCapture], asked);

            if (warnings.Count == 0)
            {
                Assert.Equal(NativeMethods.WdaExcludeFromCapture, Affinity(window));

                // Saving false shows the window to captures again; saving true hides it again.
                Assert.Empty(session.SaveSettings(session.Settings with { HideFromScreenCapture = false }));
                Assert.Equal(NativeMethods.WdaNone, Affinity(window));
                Assert.Empty(session.SaveSettings(session.Settings with { HideFromScreenCapture = true }));
                Assert.Equal(NativeMethods.WdaExcludeFromCapture, Affinity(window));

                // A save that does not change the setting asks Windows for nothing.
                Assert.Empty(session.SaveSettings(session.Settings with { PrivateHttpsPort = 12443 }));
                Assert.Equal(
                    [NativeMethods.WdaExcludeFromCapture, NativeMethods.WdaNone, NativeMethods.WdaExcludeFromCapture],
                    asked);
                Assert.Empty(warnings);
            }
            else
            {
                // A Windows build older than 10 version 2004 refuses "exclude from capture": told once.
                Assert.Equal(["Warning: " + Unavailable], warnings);
            }
        }

        // A Windows that refuses: one warning in the activity, and the call is not made again.
        using (var app = new ShellWindow(show: false))
        {
            var session = app.Session;
            var messages = new List<SessionMessage>();
            var calls = 0;
            session.MessageRaised += (_, message) => messages.Add(message);
            _ = new WindowInteropHelper(app.Window).EnsureHandle();

            app.Window.FollowCaptureSetting(
                session,
                (kind, text) => messages.Add(new SessionMessage(app.Clock.GetUtcNow(), kind, text)),
                (_, _) =>
                {
                    calls++;
                    return false;
                });

            Assert.Equal(1, calls);
            var warning = Assert.Single(messages);
            Assert.Equal((SessionMessageKind.Warning, Unavailable), (warning.Kind, warning.Text));

            Assert.Empty(session.SaveSettings(session.Settings with { HideFromScreenCapture = false }));
            Assert.Empty(session.SaveSettings(session.Settings with { HideFromScreenCapture = true }));
            Assert.Equal(1, calls);
            Assert.Single(messages);
        }
    });

    [Fact]
    public void The_real_window_is_attached_to_the_setting_and_the_self_test_window_is_not() => WpfApp.Run(() =>
    {
        using var app = new ShellWindow(show: false);
        var window = new WindowInteropHelper(app.Window).EnsureHandle();

        // Nothing followed the setting yet: the window is as Windows made it.
        Assert.Equal(NativeMethods.WdaNone, Affinity(window));

        // What App.xaml.cs does for the user's window, and never for the self-test's.
        var messages = new List<SessionMessage>();
        app.Session.MessageRaised += (_, message) => messages.Add(message);
        app.Services.Attach(app.Window);

        if (messages.Count == 0)
        {
            Assert.Equal(NativeMethods.WdaExcludeFromCapture, Affinity(window));
        }
        else
        {
            Assert.Equal(Unavailable, Assert.Single(messages).Text);
        }
    });

    private static uint Affinity(IntPtr window)
    {
        Assert.True(NativeMethods.GetWindowDisplayAffinity(window, out var affinity));
        return affinity;
    }
}
