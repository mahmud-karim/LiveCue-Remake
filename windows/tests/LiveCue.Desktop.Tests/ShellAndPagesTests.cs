using LiveCue.Desktop.ViewModels;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests;

/// <summary>The sidebar, the four page view models and the command line.</summary>
public sealed class ShellAndPagesTests
{
    [Fact]
    public async Task The_sidebar_has_the_four_views_in_order_with_their_automation_ids_and_live_selected()
    {
        await using var app = new ScriptedApp();
        var shell = app.Services.Shell;

        Assert.Equal(["Live", "Pairing", "Activity", "Settings"], shell.Pages.Select(page => page.Title));
        Assert.Equal(["NavLive", "NavPairing", "NavActivity", "NavSettings"], shell.Pages.Select(page => page.AutomationId));
        Assert.Equal(PageId.Live, shell.SelectedPage.Id);
        Assert.All(shell.Pages, page => Assert.Equal(page.Title, page.Page.Title));
        Assert.Equal("Pairing", shell.Pages[1].ToString());
    }

    [Fact]
    public async Task Navigating_selects_the_view_and_a_cleared_selection_is_refused()
    {
        await using var app = new ScriptedApp();
        var shell = app.Services.Shell;

        shell.Navigate(PageId.Settings);
        shell.SelectedPage = null!;

        // The null a list box writes when its selection is cleared never becomes the selection.
        Assert.Equal(PageId.Settings, shell.SelectedPage.Id);
        Assert.IsType<SettingsPageViewModel>(shell.SelectedPage.Page);
    }

    [Fact]
    public async Task Show_qr_is_enabled_only_while_the_relay_is_ready_and_reset_stays_disabled()
    {
        await using var app = new ScriptedApp();
        var shell = app.Services.Shell;
        var pairing = Assert.IsType<PairingPageViewModel>(shell.Pages[1].Page);

        Assert.False(pairing.ShowQrCommand.CanExecute(null));
        Assert.Equal("Start the relay first.", pairing.ShowQrCaption);
        Assert.Equal("Start the relay to show the QR", pairing.TileCaption);
        Assert.Equal("Pair your iPhone once. Restarts and showing this QR keep existing pairings valid.", pairing.Hint);

        await shell.Relay.StartCommand.ExecuteAsync(null);
        Assert.True(pairing.ShowQrCommand.CanExecute(null));
        Assert.Equal(string.Empty, pairing.ShowQrCaption);
        Assert.Equal("Show the QR to pair an iPhone", pairing.TileCaption);
        Assert.False(pairing.HideQrCommand.CanExecute(null));
        Assert.False(pairing.ResetPairingCommand.CanExecute(null));

        await shell.Relay.StopCommand.ExecuteAsync(null);
        Assert.False(pairing.ShowQrCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_activity_view_counts_its_entries_and_clear_empties_the_log()
    {
        await using var app = new ScriptedApp();
        var activity = Assert.IsType<ActivityPageViewModel>(app.Services.Shell.Pages[2].Page);

        Assert.True(activity.IsEmpty);
        Assert.Equal(string.Empty, activity.CountText);
        Assert.False(activity.ClearCommand.CanExecute(null));

        app.Services.Activity.Add("one");
        Assert.Equal("1 entry", activity.CountText);
        app.Services.Activity.Add("two");
        Assert.Equal("2 entries", activity.CountText);
        Assert.False(activity.IsEmpty);
        Assert.True(activity.ClearCommand.CanExecute(null));

        activity.ClearCommand.Execute(null);
        Assert.True(activity.IsEmpty);
        Assert.Empty(app.Services.Activity.Entries);
    }

    [Fact]
    public void The_activity_log_is_bounded_and_keeps_the_newest_entries()
    {
        var log = new ActivityLog(TimeProvider.System);

        for (var index = 0; index < ActivityLog.Capacity + 25; index++)
        {
            log.Add("entry " + index);
        }

        Assert.Equal(ActivityLog.Capacity, log.Entries.Count);
        Assert.Equal("entry 25", log.Entries[0].Message);
        Assert.Equal("entry " + (ActivityLog.Capacity + 24), log.Entries[^1].Message);
    }

    [Fact]
    public void The_settings_view_shows_private_mode_from_the_defaults()
    {
        var settings = new SettingsPageViewModel(new InMemorySettingsStore().Load());

        Assert.Equal("Private tailnet", settings.ModeText);
        Assert.Equal("11443", settings.HttpsPortText);
        Assert.Equal("/", settings.PathText);
        Assert.Equal("47841", settings.RelayPortText);
        Assert.Equal("1.0.0", settings.VersionText);
        Assert.Equal("2", settings.ProtocolText);
        Assert.Equal("MIT", settings.LicenceText);
        Assert.Null(settings.SettingsFileProblem);
    }

    [Fact]
    public void The_settings_view_shows_public_mode_and_an_unusable_settings_file()
    {
        var publicMode = new HostSettings { ConnectionMode = ConnectionMode.Public, PublicHttpsPort = 8443, PublicPathPrefix = "/cue" };
        var settings = new SettingsPageViewModel(new InMemorySettingsStore(publicMode).Load());
        var broken = new SettingsPageViewModel(
            new SettingsLoadResult(new HostSettings(), SettingsSource.DefaultsBecauseFileIsInvalid, ["relayPort must be between 1024 and 65535 and never 47831."]));

        Assert.Equal("Public Funnel", settings.ModeText);
        Assert.Equal("8443", settings.HttpsPortText);
        Assert.Equal("/cue/", settings.PathText);
        Assert.NotNull(broken.SettingsFileProblem);
        Assert.Equal("47841", broken.RelayPortText);
    }

    [Fact]
    public void No_arguments_means_the_normal_window()
    {
        var arguments = AppArguments.Parse([]);

        Assert.False(arguments.IsSelfTest);
        Assert.Null(arguments.ScreenshotDirectory);
        Assert.Equal(1, arguments.Scale);
    }

    [Fact]
    public void The_self_test_switches_are_read_and_unknown_arguments_are_ignored()
    {
        var folder = Path.Combine(Path.GetTempPath(), "LiveCue-shots");

        var arguments = AppArguments.Parse(["--unknown", "--self-test", "--screenshots", folder, "--scale", "1.5"]);

        Assert.True(arguments.IsSelfTest);
        Assert.Equal(Path.GetFullPath(folder), arguments.ScreenshotDirectory);
        Assert.Equal(1.5, arguments.Scale);
    }

    [Theory]
    [InlineData("--screenshots")]
    [InlineData("--screenshots", "--self-test")]
    [InlineData("--scale")]
    [InlineData("--scale", "2")]
    [InlineData("--scale", "big")]
    public void A_switch_without_a_usable_value_is_an_error(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => AppArguments.Parse(args));
    }
}
