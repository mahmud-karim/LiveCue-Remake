using System.ComponentModel;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Tests.Live;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Settings;

/// <summary>
/// The Settings page's view model over a <see cref="FakeSession"/> (test plan 2.8 and its security
/// tests ST-06 and ST-08; W-design 5.8; screens section 5): the form and its rules, the state of the
/// Tailscale mapping, the warnings, the assistant and speech rows. No test runs a command.
/// </summary>
public sealed class SettingsViewModelTests
{
    private const string PrivateCommand = "tailscale serve --bg --https=11443 http://127.0.0.1:47841";
    private const string PublicCommand = "tailscale funnel --bg --https=10000 --set-path=/livecue http://127.0.0.1:47841";

    [Fact]
    public void Validation_cases()
    {
        var parts = new SettingsParts();
        var settings = parts.Settings;
        var errorsChanged = new List<string?>();
        settings.ErrorsChanged += (_, change) => errorsChanged.Add(change.PropertyName);

        // The saved settings: nothing to save, nothing wrong.
        Assert.False(settings.HasErrors);
        Assert.False(settings.HasChanges);
        Assert.False(settings.SaveCommand.CanExecute(null));
        Assert.False(settings.DiscardCommand.CanExecute(null));
        Assert.Empty(settings.GetErrors(null).Cast<string>());

        // Relay port: 1024 to 65535, never 47831, never the lab's port.
        (string Text, string? Error)[] relayPorts =
        [
            (string.Empty, "Enter a port from 1024 to 65535."),
            ("abc", "Enter a port from 1024 to 65535."),
            ("80", "Enter a port from 1024 to 65535."),
            ("1023", "Enter a port from 1024 to 65535."),
            ("65536", "Enter a port from 1024 to 65535."),
            ("+47841", "Enter a port from 1024 to 65535."),
            ("47831", "Port 47831 belongs to the old LiveCue and is never used. Choose another relay port."),
            ("47842", "This port is used by the PC speech lab. Choose another relay port."),
            ("1024", null),
            (" 47900 ", null),
        ];
        foreach (var (text, error) in relayPorts)
        {
            settings.RelayPortText = text;
            Assert.Equal(error, settings.RelayPortError);
            Assert.Equal(error is null ? [] : [error], settings.GetErrors(nameof(ISettingsViewModel.RelayPortText)).Cast<string>());
            Assert.Equal(error is not null, settings.HasErrors);
            Assert.Equal(error is null, settings.SaveCommand.CanExecute(null));
            Assert.True(settings.HasChanges);
        }

        Assert.Contains(nameof(ISettingsViewModel.RelayPortText), errorsChanged);
        settings.RelayPortText = "47841";
        Assert.False(settings.HasChanges);

        // Private HTTPS port: 1 to 65535, and none of the three ports Funnel can publish.
        const string funnelPort =
            "Ports 443, 8443 and 10000 can be published by Tailscale Funnel. Choose another port for the private listener.";
        (string Text, string? Error)[] privatePorts =
        [
            ("0", "Enter a port from 1 to 65535."),
            ("70000", "Enter a port from 1 to 65535."),
            ("x", "Enter a port from 1 to 65535."),
            ("443", funnelPort),
            ("8443", funnelPort),
            ("10000", funnelPort),
            ("1", null),
            ("12443", null),
        ];
        foreach (var (text, error) in privatePorts)
        {
            settings.PrivateHttpsPortText = text;
            Assert.Equal(error, settings.PrivateHttpsPortError);
            Assert.Equal(error is null ? [] : [error], settings.GetErrors(nameof(ISettingsViewModel.PrivateHttpsPortText)).Cast<string>());
            Assert.Equal(error is null, settings.SaveCommand.CanExecute(null));
        }

        // Public Funnel: the path prefix, as a whole text.
        settings.Mode = ConnectionMode.Public;
        Assert.True(settings.IsPublicMode);
        const string badPath =
            "Use a path such as /livecue: a slash, then 1 to 32 lowercase letters, digits or hyphens, starting with a letter or digit.";
        (string Text, string? Error)[] prefixes =
        [
            ("livecue", badPath),
            ("/", badPath),
            (string.Empty, badPath),
            ("/Live", badPath),
            ("/-a", badPath),
            ("/a b", badPath),
            ("/a/b", badPath),
            ("/" + new string('a', 33), badPath),
            ("/" + new string('a', 32), null),
            ("/cue-2", null),
        ];
        foreach (var (text, error) in prefixes)
        {
            settings.PublicPathPrefix = text;
            Assert.Equal(error, settings.PublicPathPrefixError);
            Assert.Equal(error is null, settings.SaveCommand.CanExecute(null));
        }

        // A field the other mode does not show cannot keep an error nobody can see.
        settings.PublicPathPrefix = "nope";
        Assert.True(settings.HasErrors);
        settings.Mode = ConnectionMode.Private;
        Assert.Equal("/livecue", settings.PublicPathPrefix);
        Assert.False(settings.HasErrors);
        settings.PrivateHttpsPortText = "443";
        Assert.True(settings.HasErrors);
        settings.Mode = ConnectionMode.Public;
        Assert.Equal("11443", settings.PrivateHttpsPortText);
        Assert.False(settings.HasErrors);
        settings.DiscardCommand.Execute(null);
        Assert.False(settings.HasChanges);

        // Codex path: blank is automatic; otherwise the full path of an existing codex.exe.
        const string codexMissing = "No file was found at this path. Enter the full path of codex.exe, or leave the field empty.";
        parts.Files.Files.Add(@"C:\Tools\Codex\codex.exe");
        (string Text, string? Error)[] codexPaths =
        [
            ("codex.exe", codexMissing),
            (@"Tools\codex.exe", codexMissing),
            (@"C:\Tools\Codex\codex.cmd", "The file must be named codex.exe."),
            (@"C:\Tools\Codex", "The file must be named codex.exe."),
            (@"C:\Elsewhere\codex.exe", codexMissing),
            (@"C:\Tools\Codex\codex.exe", null),
            (@"C:\Tools\Codex\CODEX.EXE", null),
            ("\"C:\\Tools\\Codex\\codex.exe\"", null),
            ("   ", null),
        ];
        foreach (var (text, error) in codexPaths)
        {
            settings.CodexPathText = text;
            Assert.Equal(error, settings.CodexPathError);
            Assert.Equal(error is not null, settings.HasErrors);
        }

        settings.CodexPathText = string.Empty;

        // Lab folder: a folder on a local drive of this PC that holds the two launcher scripts.
        const string notLocal = "Choose a folder on a local drive of this PC. Network shares are not used.";
        parts.Files.Directories.Add(@"C:\Labs\empty");
        parts.Files.AddLab(@"C:\Labs\asr");
        parts.Files.AddLab(@"Z:\asr");
        (string Text, string? Error)[] labFolders =
        [
            (@"\\server\share\asr", notLocal),
            (@"\\?\C:\Labs\asr", notLocal),
            ("asr", notLocal),
            (@"C:asr", notLocal),
            (@"Z:\asr", notLocal),
            (@"C:\Labs\none", "This folder does not exist."),
            (@"C:\Labs\empty", "This folder does not hold Start-ASR-Lab.ps1 and Stop-ASR-Lab.ps1."),
            (@"C:\Labs\asr", null),
            (string.Empty, null),
        ];
        foreach (var (text, error) in labFolders)
        {
            settings.LabFolderText = text;
            Assert.Equal(error, settings.LabFolderError);
            Assert.Equal(error is null ? [] : [error], settings.GetErrors(nameof(ISettingsViewModel.LabFolderText)).Cast<string>());
        }

        // Every error at once is every error line, and Save stays disabled until the last is gone.
        settings.RelayPortText = "1";
        settings.PrivateHttpsPortText = "443";
        settings.LabFolderText = @"Z:\asr";
        Assert.Equal(3, settings.GetErrors(string.Empty).Cast<string>().Count());
        Assert.False(settings.SaveCommand.CanExecute(null));
        settings.SaveCommand.Execute(null);
        Assert.Empty(parts.Session.SavedSettings);
        settings.DiscardCommand.Execute(null);
        Assert.False(settings.HasErrors);

        // A problem the store reports at the save itself is shown on its field, too.
        settings.RelayPortText = "48000";
        parts.Session.SaveProblems = [new SettingsProblem(SettingsField.RelayPort, SettingsProblemCode.Duplicate)];
        settings.SaveCommand.Execute(null);
        Assert.Equal("This port is used by the PC speech lab. Choose another relay port.", settings.RelayPortError);
        Assert.True(settings.HasChanges);
        Assert.Equal(RelaySettings.Default.RelayPort, parts.Session.Settings.RelayPort);
    }

    [Fact]
    public void The_shown_command_is_the_planned_one()
    {
        var parts = new SettingsParts();
        var settings = parts.Settings;
        parts.Checked();
        parts.Inspect(MappingState.Absent);

        // What is shown is what the session plans for the saved settings.
        var plan = parts.Session.PlanSetUp(out _)!;
        Assert.Equal(PrivateCommand, plan.Command.DisplayText);
        Assert.Equal(MappingCommand.ApplyPrivate(11443, 47841).DisplayText, settings.MappingCommandText);
        Assert.Equal(plan.Command.DisplayText, settings.MappingCommandText);
        Assert.Equal(plan.EffectText, settings.MappingEffectText);

        // Unsaved edits do not change it: the command is that of the settings that are saved.
        settings.PrivateHttpsPortText = "12443";
        settings.RelayPortText = "48000";
        settings.Mode = ConnectionMode.Public;
        Assert.Equal(PrivateCommand, settings.MappingCommandText);

        // Saved: the command is the new one, and the dialog shows exactly it.
        settings.SaveCommand.Execute(null);
        parts.Inspect(MappingState.Absent);
        Assert.Equal("tailscale funnel --bg --https=10000 --set-path=/livecue http://127.0.0.1:48000", settings.MappingCommandText);
        Assert.Equal(parts.Session.PlanSetUp(out _)!.Command.DisplayText, settings.MappingCommandText);
        settings.SetUpMappingCommand.Execute(null);
        var dialog = Assert.IsType<MappingDialogViewModel>(Assert.Single(parts.Dialogs.Shown));
        Assert.Equal(settings.MappingCommandText, dialog.CommandText);
        Assert.Equal(settings.MappingEffectText, dialog.EffectText);
        Assert.Equal(MappingAction.ApplyPublic, dialog.Action);
        Assert.Empty(parts.Session.ExecutedPlans);
        dialog.CancelCommand.Execute(null);

        // No valid command exists: nothing is shown, and nothing can be set up.
        parts.Session.PlanRefusal = MappingRefusal.InvalidSettings;
        parts.Inspect(MappingState.Absent);
        Assert.Equal(string.Empty, settings.MappingCommandText);
        Assert.Equal(string.Empty, settings.MappingEffectText);
        Assert.False(settings.CanSetUpMapping);

        // The saved default in Public mode.
        var funnel = new SettingsParts(RelaySettings.Default with { ConnectionMode = ConnectionMode.Public });
        Assert.Equal(PublicCommand, funnel.Settings.MappingCommandText);
    }

    [Fact]
    public void Set_up_gating()
    {
        var parts = new SettingsParts();
        var settings = parts.Settings;
        var changed = new List<string?>();
        settings.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        void Expect(StatusMark mark, StatusTone tone, string text, string? caption, bool canSetUp)
        {
            Assert.Equal(new StatusIndicator(mark, tone, text), settings.MappingStatus);
            Assert.Equal(caption, settings.MappingCaption);
            Assert.Equal(canSetUp, settings.CanSetUpMapping);
            Assert.Equal(canSetUp, settings.SetUpMappingCommand.CanExecute(null));
        }

        // Nothing was inspected yet.
        Expect(StatusMark.Ring, StatusTone.Offline, "Not checked", null, canSetUp: false);

        // Not set up: the one state in which the button works.
        parts.Checked();
        parts.Inspect(MappingState.Absent);
        Expect(StatusMark.Dot, StatusTone.Warning, "Not set up", null, canSetUp: true);
        Assert.Contains(nameof(ISettingsViewModel.MappingStatus), changed);
        Assert.Contains(nameof(ISettingsViewModel.CanSetUpMapping), changed);

        // Unsaved changes: the command shown would not be the one for what the form says.
        settings.PrivateHttpsPortText = "12443";
        Expect(StatusMark.Dot, StatusTone.Warning, "Not set up", "Save the settings first.", canSetUp: false);
        settings.DiscardCommand.Execute(null);
        Expect(StatusMark.Dot, StatusTone.Warning, "Not set up", null, canSetUp: true);
        settings.HideFromScreenCapture = false;
        Assert.False(settings.CanSetUpMapping);
        settings.DiscardCommand.Execute(null);

        // Blocked, each with its reason.
        (MappingRefusal Refusal, string Reason)[] blocked =
        [
            (MappingRefusal.PrivatePortOccupied, "Port 11443 already has other Tailscale handlers. Choose another private HTTPS port."),
            (MappingRefusal.PrivatePortHasFunnel, "Port 11443 has Funnel turned on. A private listener there would turn Funnel off for the whole port. Choose another private HTTPS port."),
        ];
        foreach (var (refusal, reason) in blocked)
        {
            parts.Inspect(MappingState.Blocked, refusal);
            Expect(StatusMark.Alert, StatusTone.Danger, "Blocked", reason, canSetUp: false);
        }

        // Unknown: Tailscale's configuration could not be read.
        foreach (var refusal in new[] { MappingRefusal.StatusUnavailable, MappingRefusal.StatusUnreadable })
        {
            parts.Inspect(MappingState.Unknown, refusal, TailscaleReadFailure.Failed);
            Expect(
                StatusMark.Alert,
                StatusTone.Danger,
                "LiveCue could not read Tailscale's configuration",
                "Check that Tailscale is running, then choose Check again.",
                canSetUp: false);
        }

        // Already there, LiveCue's own or not.
        parts.Inspect(MappingState.Present);
        Expect(StatusMark.Dot, StatusTone.Success, "Active", null, canSetUp: false);
        parts.Inspect(MappingState.PresentNotOwned);
        Expect(
            StatusMark.Dot,
            StatusTone.Success,
            "Active, not created by LiveCue",
            "This mapping was not created by LiveCue, so LiveCue will not remove it.",
            canSetUp: false);
        Assert.Null(settings.CurrentOwnedMapping);

        // Tailscale offline or missing: the status says so, with the fix, whatever was inspected.
        parts.Inspect(MappingState.Absent);
        parts.Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.TailscaleOnline] = PrerequisiteState.Failed,
            [PrerequisiteId.TailscaleDnsName] = PrerequisiteState.NotChecked,
        });
        Expect(
            StatusMark.Alert, StatusTone.Danger, "Tailscale is not online", "Open Tailscale and connect, then choose Check again.", canSetUp: false);
        parts.Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.TailscaleInstalled] = PrerequisiteState.Failed,
            [PrerequisiteId.TailscaleOnline] = PrerequisiteState.NotChecked,
            [PrerequisiteId.TailscaleDnsName] = PrerequisiteState.NotChecked,
        });
        Expect(
            StatusMark.Alert, StatusTone.Danger, "Tailscale is not installed", "Install Tailscale and sign in, then choose Check again.", canSetUp: false);
        parts.Checked();
        parts.Inspect(MappingState.Unknown, MappingRefusal.TailscaleNotInstalled, TailscaleReadFailure.NotInstalled);
        Expect(
            StatusMark.Alert, StatusTone.Danger, "Tailscale is not installed", "Install Tailscale and sign in, then choose Check again.", canSetUp: false);

        // Public mode has its own reasons.
        var funnel = new SettingsParts(RelaySettings.Default with { ConnectionMode = ConnectionMode.Public });
        funnel.Checked();
        funnel.Inspect(MappingState.Blocked, MappingRefusal.PublicPortWouldPublishOthers);
        Assert.Equal("Blocked", funnel.Settings.MappingStatus.Text);
        Assert.Equal(
            "Port 10000 has tailnet-only handlers. Turning Funnel on there would publish them to the internet. Choose a port that already has Funnel on, or an unused one.",
            funnel.Settings.MappingCaption);
        funnel.Inspect(MappingState.Blocked, MappingRefusal.PublicMountOccupied);
        Assert.Equal("The path /livecue on port 10000 is already used by something else. Choose another path.", funnel.Settings.MappingCaption);
        Assert.False(funnel.Settings.CanSetUpMapping);

        // "Check again" only reads.
        settings.CheckMappingCommand.Execute(null);
        Assert.Equal(1, parts.Session.CheckAgainCalls);
        Assert.Empty(parts.Session.ExecutedPlans);
        Assert.Empty(parts.Dialogs.Shown);
    }

    [Fact]
    public void Save_writes_no_mapping()
    {
        var ledger = new[] { SettingsParts.Owned(ConnectionMode.Private, 11443) };
        var saved = RelaySettings.Default with { OwnedMappings = ledger, CodexContainmentCheckedVersion = "1.2.3", AsrLabPort = 47850 };
        var parts = new SettingsParts(saved);
        var settings = parts.Settings;
        parts.Files.Files.Add(@"C:\Tools\Codex\codex.exe");
        parts.Files.AddLab(@"C:\Labs\asr");
        parts.Checked();
        parts.Inspect(MappingState.Present);

        settings.Mode = ConnectionMode.Public;
        settings.RelayPortText = "48000";
        settings.PublicHttpsPort = 8443;
        settings.PublicPathPrefix = "/cue";
        settings.CodexPathText = @"C:\Tools\Codex\codex.exe";
        settings.LabFolderText = @"C:\Labs\asr";
        Assert.True(settings.HasChanges);
        Assert.True(settings.SaveCommand.CanExecute(null));
        Assert.Empty(parts.Session.SavedSettings);

        settings.SaveCommand.Execute(null);

        // One save, of exactly the form, with everything the page does not edit carried over.
        var written = Assert.Single(parts.Session.SavedSettings);
        Assert.True(written.SameValues(saved with
        {
            ConnectionMode = ConnectionMode.Public,
            RelayPort = 48000,
            PublicHttpsPort = 8443,
            PublicPathPrefix = "/cue",
            CodexExecutablePath = @"C:\Tools\Codex\codex.exe",
            AsrLabFolder = @"C:\Labs\asr",
        }));
        Assert.Same(ledger, written.OwnedMappings);
        Assert.False(settings.HasChanges);
        Assert.False(settings.SaveCommand.CanExecute(null));
        Assert.Equal("48000", settings.RelayPortText);

        // Saving ran no mapping command and opened no dialog; Tailscale was only read again.
        Assert.Empty(parts.Session.ExecutedPlans);
        Assert.Empty(parts.Dialogs.Shown);
        Assert.Equal(1, parts.Session.CheckAgainCalls);
        Assert.Equal(0, parts.Processes.Calls);

        // Discard throws the edits away and saves nothing.
        settings.RelayPortText = "49000";
        settings.Mode = ConnectionMode.Private;
        Assert.True(settings.DiscardCommand.CanExecute(null));
        settings.DiscardCommand.Execute(null);
        Assert.Equal("48000", settings.RelayPortText);
        Assert.Equal(ConnectionMode.Public, settings.Mode);
        Assert.False(settings.HasChanges);
        Assert.Single(parts.Session.SavedSettings);
        Assert.Equal(1, parts.Session.CheckAgainCalls);

        // A mapping command that saved its ledger does not disturb what is being typed.
        settings.RelayPortText = "49000";
        parts.Session.Settings = parts.Session.Settings with { OwnedMappings = [] };
        Assert.Equal("49000", settings.RelayPortText);
        Assert.True(settings.HasChanges);
        settings.DiscardCommand.Execute(null);

        // Without edits the form follows the saved settings.
        parts.Session.Settings = parts.Session.Settings with { RelayPort = 50000 };
        Assert.Equal("50000", settings.RelayPortText);
        Assert.False(settings.HasChanges);
    }

    [Fact]
    public void Stale_rows()
    {
        var current = SettingsParts.Owned(ConnectionMode.Private, 11443);
        var otherMode = SettingsParts.Owned(ConnectionMode.Public, 10000, "/livecue");
        var oldPort = SettingsParts.Owned(ConnectionMode.Private, 12443);
        var parts = new SettingsParts(RelaySettings.Default with { OwnedMappings = [current, otherMode, oldPort] });
        var settings = parts.Settings;
        Assert.Empty(settings.StaleMappings);

        // The name of this PC is not known yet: the rows say where the mappings are.
        parts.Inspect(MappingState.Present, stale: [otherMode, oldPort]);
        Assert.Equal(
            [
                new StaleMappingRow(otherMode, "LiveCue's Public Funnel mapping at port 10000, path /livecue still exists."),
                new StaleMappingRow(oldPort, "LiveCue's Private tailnet mapping at port 12443, path / still exists."),
            ],
            settings.StaleMappings);

        // With the name: the address each was reached at.
        parts.Checked();
        Assert.Equal(
            [
                "LiveCue's Public Funnel mapping at https://pc.example.test:10000/livecue/ still exists.",
                "LiveCue's Private tailnet mapping at https://pc.example.test:12443/ still exists.",
            ],
            settings.StaleMappings.Select(row => row.Text));

        // Each has "Remove…", which opens the dialog with that mapping's removal command.
        foreach (var (row, command, action) in new[]
                 {
                     (settings.StaleMappings[0], "tailscale funnel --https=10000 --set-path=/livecue off", MappingAction.RemovePublic),
                     (settings.StaleMappings[1], "tailscale serve --https=12443 --set-path=/ off", MappingAction.RemovePrivate),
                 })
        {
            Assert.True(settings.RemoveMappingCommand.CanExecute(row.Entry));
            settings.RemoveMappingCommand.Execute(row.Entry);
            var dialog = Assert.IsType<MappingDialogViewModel>(parts.Dialogs.Current);
            Assert.Equal(action, dialog.Action);
            Assert.True(dialog.IsDestructive);
            Assert.Equal(command, dialog.CommandText);
            dialog.CancelCommand.Execute(null);
        }

        // The selected mode's own mapping is not a warning; it has the "Remove…" of the card.
        Assert.Equal(current, settings.CurrentOwnedMapping);
        settings.RemoveMappingCommand.Execute(settings.CurrentOwnedMapping);
        Assert.Equal("tailscale serve --https=11443 --set-path=/ off", Assert.IsType<MappingDialogViewModel>(parts.Dialogs.Current).CommandText);
        parts.Dialogs.Current!.CancelCommand.Execute(null);

        // Nothing else can be planned for removal: not a mapping that is not listed, not another kind of parameter.
        var shown = parts.Dialogs.Shown.Count;
        var stranger = SettingsParts.Owned(ConnectionMode.Private, 13443);
        Assert.False(settings.RemoveMappingCommand.CanExecute(stranger));
        settings.RemoveMappingCommand.Execute(stranger);
        settings.RemoveMappingCommand.Execute(null);
        settings.RemoveMappingCommand.Execute("tailscale serve reset");
        Assert.Equal(shown, parts.Dialogs.Shown.Count);

        // Removed in Tailscale and inspected again: the rows are gone, and so is the card's button.
        parts.Session.Settings = parts.Session.Settings with { OwnedMappings = [] };
        parts.Inspect(MappingState.Absent);
        Assert.Empty(settings.StaleMappings);
        Assert.Null(settings.CurrentOwnedMapping);
        Assert.False(settings.RemoveMappingCommand.CanExecute(otherMode));
        Assert.Empty(parts.Session.ExecutedPlans);
    }

    [Fact]
    public void Unowned_exposure_rows()
    {
        var ours = SettingsParts.Owned(ConnectionMode.Private, 11443);
        var parts = new SettingsParts(RelaySettings.Default with { OwnedMappings = [ours] });
        var settings = parts.Settings;
        parts.Checked();
        Assert.Empty(settings.UnownedExposures);
        Assert.Null(settings.ExposureUnknownText);

        var funnel = new ExposedHandler(443, "/relay", FunnelOn: true, Foreground: false, LedgerEntry: null);
        var tailnet = new ExposedHandler(8080, "/", FunnelOn: false, Foreground: false, LedgerEntry: null);
        var session = new ExposedHandler(9443, "/", FunnelOn: true, Foreground: true, LedgerEntry: null);
        var odd = new ExposedHandler(10000, "/a b;rm", FunnelOn: true, Foreground: false, LedgerEntry: null);
        var owned = new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, LedgerEntry: ours);
        parts.Inspect(MappingState.Present, exposure: [funnel, tailnet, session, odd, owned]);

        // One row per handler that forwards to the relay and is not in the ledger; LiveCue's own is not one.
        Assert.Equal(
            [
                new UnownedExposureRow(
                    funnel,
                    "A Tailscale Funnel mapping on port 443, path /relay, publishes this relay on the internet. LiveCue did not create it and will not remove it.",
                    "tailscale funnel --https=443 --set-path=/relay off"),
                new UnownedExposureRow(
                    tailnet,
                    "A Tailscale mapping on port 8080, path /, forwards to this relay inside your tailnet. LiveCue did not create it and will not remove it.",
                    "tailscale serve --https=8080 --set-path=/ off"),
                new UnownedExposureRow(
                    session,
                    "A Tailscale Funnel mapping on port 9443, path /, publishes this relay on the internet. LiveCue did not create it and will not remove it. It belongs to a running Tailscale session or service; end that to remove it.",
                    string.Empty),
                new UnownedExposureRow(
                    odd,
                    "A Tailscale Funnel mapping on port 10000, path /a b;rm, publishes this relay on the internet. LiveCue did not create it and will not remove it.",
                    string.Empty),
            ],
            settings.UnownedExposures);
        Assert.All(settings.UnownedExposures, row =>
        {
            Assert.Equal(MappingTexts.Exposure(row.Handler), row.Text);
            Assert.Equal(MappingTexts.ManualRemoval(row.Handler), row.CommandText);
        });

        // The page offers no way to run those commands: a row is not a removal parameter, an entry made
        // up to look like one is refused, and the view model has no other command that removes.
        var dialogs = parts.Dialogs.Shown.Count;
        foreach (var row in settings.UnownedExposures)
        {
            var lookalike = new OwnedMapping(
                row.Handler.FunnelOn ? ConnectionMode.Public : ConnectionMode.Private,
                row.Handler.HttpsPort,
                row.Handler.Mount,
                SettingsParts.Target,
                DateTimeOffset.UnixEpoch);
            foreach (var parameter in new object[] { row, row.Handler, row.CommandText, lookalike })
            {
                Assert.False(settings.RemoveMappingCommand.CanExecute(parameter));
                settings.RemoveMappingCommand.Execute(parameter);
            }
        }

        Assert.Equal(dialogs, parts.Dialogs.Shown.Count);
        Assert.Empty(parts.Session.ExecutedPlans);
        Assert.Equal(
            ["RemoveMappingCommand"],
            typeof(ISettingsViewModel).GetProperties()
                .Where(property => property.PropertyType == typeof(System.Windows.Input.ICommand)
                    && property.Name.Contains("Remove", StringComparison.Ordinal))
                .Select(property => property.Name));

        // A mapping that works but is not in the ledger keeps its status and is listed, too.
        var notOurs = new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, LedgerEntry: null);
        parts.Inspect(MappingState.PresentNotOwned, exposure: [notOurs]);
        Assert.Equal("Active, not created by LiveCue", settings.MappingStatus.Text);
        Assert.Equal("tailscale serve --https=11443 --set-path=/ off", Assert.Single(settings.UnownedExposures).CommandText);
        Assert.Null(settings.CurrentOwnedMapping);

        // The serve status could not be read: what is exposed is unknown, not nothing.
        foreach (var failure in new[] { TailscaleReadFailure.Failed, TailscaleReadFailure.TimedOut, TailscaleReadFailure.Unparseable })
        {
            parts.Inspect(MappingState.Unknown, MappingRefusal.StatusUnavailable, failure);
            Assert.Empty(settings.UnownedExposures);
            Assert.Equal(
                "Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed. Choose Check again.",
                settings.ExposureUnknownText);
        }

        // Without Tailscale nothing can expose the relay.
        parts.Inspect(MappingState.Unknown, MappingRefusal.TailscaleNotInstalled, TailscaleReadFailure.NotInstalled);
        Assert.Null(settings.ExposureUnknownText);
        parts.Inspect(MappingState.Absent);
        Assert.Null(settings.ExposureUnknownText);
    }

    [Fact]
    public void Loaded_model_cases()
    {
        var parts = new SettingsParts();
        var settings = parts.Settings;
        var now = ManualTimeProvider.DefaultStart;
        var changed = 0;
        settings.PropertyChanged += (_, change) => changed += change.PropertyName == nameof(ISettingsViewModel.LoadedModelStatus) ? 1 : 0;

        void Model(LocalModelState state, string? model, string message = "") =>
            parts.Session.RaiseRelayEvent(new LocalModelChanged(now, new LocalModelStatus(state, model, state == LocalModelState.Starting, 0, message), message));

        void Speech(SpeechStreamPhase phase, string model, string? code = null) =>
            parts.Session.RaiseRelayEvent(new SpeechStreamChanged(now, phase, model, code, "text"));

        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "Not reported yet"), settings.LoadedModelStatus);

        Model(LocalModelState.Starting, "nemotron");
        Assert.Equal(new StatusIndicator(StatusMark.Spinner, StatusTone.Live, "Nemotron · loading"), settings.LoadedModelStatus);
        Model(LocalModelState.Ready, "nemotron");
        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Success, "Nemotron · ready"), settings.LoadedModelStatus);
        Assert.Equal(2, changed);

        // A speech stream: loading says nothing new; ready means the model is in use; then it is free again.
        Speech(SpeechStreamPhase.Loading, "nemotron");
        Assert.Equal("Nemotron · ready", settings.LoadedModelStatus.Text);
        Speech(SpeechStreamPhase.Ready, "nemotron");
        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Live, "Nemotron · in use"), settings.LoadedModelStatus);
        Speech(SpeechStreamPhase.Stopped, "nemotron");
        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Success, "Nemotron · ready"), settings.LoadedModelStatus);
        Speech(SpeechStreamPhase.Ready, "qwen3");
        Assert.Equal("Qwen3 · in use", settings.LoadedModelStatus.Text);
        Speech(SpeechStreamPhase.Failed, "qwen3", "LAB_STREAM_FAILED");
        Assert.Equal("Qwen3 · ready", settings.LoadedModelStatus.Text);

        // Stopped is "None"; an error is the relay's own message.
        Model(LocalModelState.Stopping, "qwen3");
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "None"), settings.LoadedModelStatus);
        Model(LocalModelState.Starting, "qwen3");
        Assert.Equal("Qwen3 · loading", settings.LoadedModelStatus.Text);
        Model(LocalModelState.Error, "qwen3", "Docker Desktop did not start in time.");
        Assert.Equal(
            new StatusIndicator(StatusMark.Alert, StatusTone.Danger, "Docker Desktop did not start in time."),
            settings.LoadedModelStatus);
        Model(LocalModelState.Stopped, null);
        Assert.Equal("None", settings.LoadedModelStatus.Text);

        // A model the app has no name for is shown as the relay names it.
        Model(LocalModelState.Ready, "whisper-x");
        Assert.Equal("whisper-x · ready", settings.LoadedModelStatus.Text);

        // Other events say nothing about the model.
        parts.Session.RaiseRelayEvent(new SelfCheckCompleted(now, SelfCheckResult.Verified));
        Assert.Equal("whisper-x · ready", settings.LoadedModelStatus.Text);

        // "Since it started": another relay has reported nothing yet.
        parts.Session.RaiseSignal(SessionSignal.RelayEnded);
        Assert.Equal("Not reported yet", settings.LoadedModelStatus.Text);
        Model(LocalModelState.Ready, "nemotron");
        parts.Session.RaiseSignal(SessionSignal.RelayStarting);
        Assert.Equal("Not reported yet", settings.LoadedModelStatus.Text);
    }

    [Fact]
    public void Hide_from_capture_is_a_form_field()
    {
        var parts = new SettingsParts();
        var settings = parts.Settings;
        var changed = new List<string?>();
        settings.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        // On by default, like the setting.
        Assert.True(RelaySettings.Default.HideFromScreenCapture);
        Assert.True(settings.HideFromScreenCapture);
        Assert.False(settings.HasChanges);

        // Switching it changes the form and nothing else: nothing is saved by the switch itself.
        settings.HideFromScreenCapture = false;
        Assert.False(settings.HideFromScreenCapture);
        Assert.True(settings.HasChanges);
        Assert.True(settings.SaveCommand.CanExecute(null));
        Assert.Contains(nameof(ISettingsViewModel.HideFromScreenCapture), changed);
        Assert.Contains(nameof(ISettingsViewModel.HasChanges), changed);
        Assert.Empty(parts.Session.SavedSettings);
        Assert.True(parts.Session.Settings.HideFromScreenCapture);

        // Discard puts the saved value back.
        settings.DiscardCommand.Execute(null);
        Assert.True(settings.HideFromScreenCapture);
        Assert.False(settings.HasChanges);
        Assert.Empty(parts.Session.SavedSettings);

        // Save writes it, with the rest of the form.
        settings.HideFromScreenCapture = false;
        settings.RelayPortText = "48000";
        settings.SaveCommand.Execute(null);
        var written = Assert.Single(parts.Session.SavedSettings);
        Assert.False(written.HideFromScreenCapture);
        Assert.Equal(48000, written.RelayPort);
        Assert.False(parts.Session.Settings.HideFromScreenCapture);
        Assert.False(settings.HideFromScreenCapture);
        Assert.False(settings.HasChanges);

        // And back on.
        settings.HideFromScreenCapture = true;
        settings.SaveCommand.Execute(null);
        Assert.True(parts.Session.SavedSettings[^1].HideFromScreenCapture);
        Assert.Equal(2, parts.Session.SavedSettings.Count);
    }

    [Fact]
    public void Assistant_rows_and_mark_as_checked()
    {
        var parts = new SettingsParts(RelaySettings.Default with { CodexContainmentCheckedVersion = "1.2.2" });
        var settings = parts.Settings;

        // No check has run yet.
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "Not checked"), settings.CodexPathStatus);
        Assert.Null(settings.CodexPathDetail);
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "Not checked"), settings.CodexLoginStatus);
        Assert.Null(settings.CodexLoginDetail);
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "Unknown"), settings.CodexVersionStatus);
        Assert.Null(settings.CodexVersionDetail);
        Assert.False(settings.CanMarkContainmentChecked);
        Assert.False(settings.MarkContainmentCheckedCommand.CanExecute(null));

        // Opening the page runs a check only when none has run.
        settings.OnOpened();
        Assert.Equal(1, parts.Session.CheckAgainCalls);

        // Codex missing: the fix; sign-in and version are not checked.
        parts.Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.CodexFound] = PrerequisiteState.Failed,
            [PrerequisiteId.CodexSignedIn] = PrerequisiteState.NotChecked,
        });
        settings.OnOpened();
        Assert.Equal(1, parts.Session.CheckAgainCalls);
        Assert.Equal(new StatusIndicator(StatusMark.Alert, StatusTone.Danger, "Not found"), settings.CodexPathStatus);
        Assert.Equal("Install or update the Codex desktop app, then open it once and sign in.", settings.CodexPathDetail);
        Assert.Equal(new StatusIndicator(StatusMark.Ring, StatusTone.Offline, "Not checked"), settings.CodexLoginStatus);
        Assert.Null(settings.CodexLoginDetail);
        Assert.Equal("Unknown", settings.CodexVersionStatus.Text);
        Assert.Null(settings.CodexVersionDetail);
        Assert.False(settings.CanMarkContainmentChecked);

        // Found but not signed in.
        parts.Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>
        {
            [PrerequisiteId.CodexSignedIn] = PrerequisiteState.Failed,
        });
        Assert.Equal(new StatusIndicator(StatusMark.Check, StatusTone.Success, "Found"), settings.CodexPathStatus);
        Assert.Equal(@"C:\Tools\Codex\codex.exe", settings.CodexPathDetail);
        Assert.Equal(new StatusIndicator(StatusMark.Alert, StatusTone.Danger, "Not signed in"), settings.CodexLoginStatus);
        Assert.Equal("Open Codex and sign in with ChatGPT, then choose Check again.", settings.CodexLoginDetail);

        // Signed in, at a version the containment check was not run for: a warning, never a refusal.
        parts.Checked("1.2.3");
        Assert.Equal(new StatusIndicator(StatusMark.Check, StatusTone.Success, "Signed in"), settings.CodexLoginStatus);
        Assert.Null(settings.CodexLoginDetail);
        Assert.Equal(new StatusIndicator(StatusMark.Alert, StatusTone.Warning, "1.2.3"), settings.CodexVersionStatus);
        Assert.Equal(
            "Codex is version 1.2.3; the containment check was last run for 1.2.2. Run the containment check, then choose Mark as checked in Settings.",
            settings.CodexVersionDetail);
        Assert.True(settings.CanMarkContainmentChecked);

        // A version that could not be read cannot be marked.
        parts.Checked(codexVersion: null);
        Assert.Equal(new StatusIndicator(StatusMark.Alert, StatusTone.Warning, "Unknown"), settings.CodexVersionStatus);
        Assert.Equal(
            "Codex's version could not be read; the containment check was last run for 1.2.2. Run the containment check, then choose Mark as checked in Settings.",
            settings.CodexVersionDetail);
        Assert.False(settings.CanMarkContainmentChecked);
        settings.MarkContainmentCheckedCommand.Execute(null);
        Assert.Empty(parts.Session.SavedSettings);

        // "Mark as checked" records the version on the saved settings. What is typed in the form is
        // neither saved with it nor lost, and a new check follows.
        parts.Checked("1.2.3");
        settings.RelayPortText = "48000";
        settings.HideFromScreenCapture = false;
        var checks = parts.Session.CheckAgainCalls;
        settings.MarkContainmentCheckedCommand.Execute(null);
        var written = Assert.Single(parts.Session.SavedSettings);
        Assert.True(written.SameValues(RelaySettings.Default with { CodexContainmentCheckedVersion = "1.2.3" }));
        Assert.Equal(checks + 1, parts.Session.CheckAgainCalls);
        Assert.Equal("48000", settings.RelayPortText);
        Assert.False(settings.HideFromScreenCapture);
        Assert.True(settings.HasChanges);
        Assert.Equal(new StatusIndicator(StatusMark.Check, StatusTone.Success, "1.2.3"), settings.CodexVersionStatus);
        Assert.Equal("Containment check run for this version.", settings.CodexVersionDetail);
        Assert.False(settings.CanMarkContainmentChecked);
        Assert.False(settings.MarkContainmentCheckedCommand.CanExecute(null));

        // Saving the form afterwards keeps the recorded version.
        settings.SaveCommand.Execute(null);
        Assert.Equal("1.2.3", parts.Session.SavedSettings[^1].CodexContainmentCheckedVersion);
        Assert.Equal(48000, parts.Session.SavedSettings[^1].RelayPort);

        // "Check again" checks; while a check runs the page says so.
        parts.Session.Gate = new TaskCompletionSource();
        Assert.False(settings.IsCheckingAssistant);
        settings.CheckAssistantCommand.Execute(null);
        Assert.True(settings.IsCheckingAssistant);
        parts.Session.Gate.SetResult();
        Assert.False(settings.IsCheckingAssistant);
        parts.Session.State = RelayUiState.Checking;
        Assert.True(settings.IsCheckingAssistant);
        Assert.Empty(parts.Session.ExecutedPlans);
    }

    [Fact]
    public void Address_notices_speech_rows_and_about()
    {
        var parts = new SettingsParts();
        var settings = parts.Settings;
        var changed = new List<string?>();
        ((INotifyPropertyChanged)settings).PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        // The PC address: unknown until Tailscale's name is; then it follows the form as it stands.
        Assert.Equal("Shown when Tailscale is connected", settings.PcAddress);
        Assert.False(settings.HasPcAddress);
        Assert.False(settings.CopyAddressCommand.CanExecute(null));
        parts.Checked();
        Assert.Equal("https://pc.example.test:11443/", settings.PcAddress);
        Assert.True(settings.HasPcAddress);
        settings.PrivateHttpsPortText = "12443";
        Assert.Equal("https://pc.example.test:12443/", settings.PcAddress);
        settings.Mode = ConnectionMode.Public;
        Assert.Equal("https://pc.example.test:10000/livecue/", settings.PcAddress);
        Assert.Equal(
            "Reachable from the internet over HTTPS and protected by the pairing token, not by tailnet membership. The iPhone needs only an internet connection.",
            settings.ModeExplanation);
        settings.PublicHttpsPort = 443;
        settings.PublicPathPrefix = "/cue";
        Assert.Equal("https://pc.example.test:443/cue/", settings.PcAddress);
        settings.CopyAddressCommand.Execute(null);
        Assert.Equal("https://pc.example.test:443/cue/", parts.Clipboard.Last);
        Assert.Equal(["Copied"], parts.Toasts.Texts);
        settings.PublicPathPrefix = "not a path";
        Assert.Equal("Shown when Tailscale is connected", settings.PcAddress);
        Assert.False(settings.CopyAddressCommand.CanExecute(null));
        settings.DiscardCommand.Execute(null);
        Assert.Equal(
            "Reachable only from devices signed in to your tailnet. The iPhone needs Tailscale connected.",
            settings.ModeExplanation);

        // The two notices of the Connection panel.
        Assert.Null(settings.RestartNotice);
        Assert.Null(settings.SettingsFileNotice);
        parts.Session.SettingsDifferFromRunning = true;
        Assert.Equal(
            new BannerModel(BannerTone.Warning, null, "The relay is still using the previous connection settings. Stop and start it to apply the change."),
            settings.RestartNotice);
        Assert.Contains(nameof(ISettingsViewModel.RestartNotice), changed);
        foreach (var state in new[] { SettingsFileState.Invalid, SettingsFileState.Unreadable })
        {
            parts.Session.SettingsLoad = new SettingsLoadResult(RelaySettings.Default, state, []);
            Assert.Equal(
                new BannerModel(BannerTone.Warning, null, "LiveCue could not read its settings file and is using the defaults. Saving here replaces the file."),
                settings.SettingsFileNotice);
        }

        parts.Session.SettingsLoad = new SettingsLoadResult(RelaySettings.Default, SettingsFileState.Loaded, []);
        Assert.Null(settings.SettingsFileNotice);

        // The lab folder: not set up, with the hint; then the saved folder, once it is a lab folder.
        Assert.Equal(new StatusIndicator(StatusMark.Dot, StatusTone.Warning, "Not set up"), settings.LabFolderStatus);
        Assert.Null(settings.LabFolderDetail);
        Assert.Equal(
            "PC speech models are optional. Without the lab folder, choose Meta Muse, Voz or Parakeet on the iPhone.",
            settings.SpeechCaption);
        parts.Files.AddLab(@"C:\Labs\asr");
        settings.LabFolderText = @"C:\Labs\asr";
        Assert.Null(settings.LabFolderDetail);
        settings.SaveCommand.Execute(null);
        Assert.Equal(new StatusIndicator(StatusMark.Check, StatusTone.Success, "Found"), settings.LabFolderStatus);
        Assert.Equal(@"C:\Labs\asr", settings.LabFolderDetail);
        Assert.Equal(
            "Models are started and stopped from the iPhone: Settings, Transcription, PC speech model. Start opens Docker Desktop if needed; one model runs at a time.",
            settings.SpeechCaption);

        // About: the version, and two links that go through the launcher.
        Assert.Equal("1.0.0", settings.Version);
        settings.OpenSourceCommand.Execute(null);
        Assert.Equal([new Uri("https://github.com/mahmud-karim/LiveCue-Remake")], parts.Launcher.Urls);
        settings.OpenNoticesCommand.Execute(null);
        Assert.Equal("THIRD-PARTY-NOTICES.md", Path.GetFileName(Assert.Single(parts.Launcher.Files)));
        Assert.Equal(0, parts.Processes.Calls);
    }
}
