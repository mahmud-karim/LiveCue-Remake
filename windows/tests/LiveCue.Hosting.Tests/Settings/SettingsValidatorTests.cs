using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Settings;

/// <summary>
/// The rules of architecture 3.5 (table in W-design 4.14). The cases are the spike's invalid settings,
/// now against the validator and its problem codes; work package WP-W06 owns this file.
/// </summary>
public sealed class SettingsValidatorTests
{
    private const string Codex = @"C:\Tools\codex.exe";
    private const string Lab = @"C:\Tools\asr";

    [Fact]
    public void The_defaults_are_valid_and_use_port_47841()
    {
        var settings = RelaySettings.Default;

        Assert.Empty(Validator().Validate(settings));
        Assert.Equal(1, settings.SchemaVersion);
        Assert.Equal(47841, settings.RelayPort);
        Assert.Equal(ConnectionMode.Private, settings.ConnectionMode);
        Assert.Equal(11443, settings.PrivateHttpsPort);
        Assert.Equal(10000, settings.PublicHttpsPort);
        Assert.Equal("/livecue", settings.PublicPathPrefix);
        Assert.Null(settings.CodexExecutablePath);
        Assert.Null(settings.AsrLabFolder);
        Assert.Equal(47842, settings.AsrLabPort);
        Assert.Empty(settings.OwnedMappings);
        Assert.True(settings.SameValues(new RelaySettings()));
    }

    public static TheoryData<RelaySettings, SettingsField, SettingsProblemCode> Rules => new()
    {
        { new RelaySettings { SchemaVersion = 2 }, SettingsField.SchemaVersion, SettingsProblemCode.UnsupportedVersion },
        { new RelaySettings { RelayPort = 1023 }, SettingsField.RelayPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { RelayPort = 65536 }, SettingsField.RelayPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { RelayPort = 0 }, SettingsField.RelayPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { RelayPort = 47831 }, SettingsField.RelayPort, SettingsProblemCode.Forbidden },
        { new RelaySettings { RelayPort = 47842 }, SettingsField.RelayPort, SettingsProblemCode.Duplicate },
        { new RelaySettings { ConnectionMode = (ConnectionMode)7 }, SettingsField.ConnectionMode, SettingsProblemCode.NotAllowed },
        { new RelaySettings { PrivateHttpsPort = 0 }, SettingsField.PrivateHttpsPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { PrivateHttpsPort = 10000 }, SettingsField.PrivateHttpsPort, SettingsProblemCode.Duplicate },
        { new RelaySettings { PublicHttpsPort = 444 }, SettingsField.PublicHttpsPort, SettingsProblemCode.NotAllowed },
        { new RelaySettings { PublicPathPrefix = "/" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "livecue" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "/LiveCue" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "/-livecue" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "/" + new string('a', 33) }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { CodexExecutablePath = "codex.exe" }, SettingsField.CodexExecutablePath, SettingsProblemCode.NotFound },
        { new RelaySettings { CodexExecutablePath = @"C:\Tools\other.exe" }, SettingsField.CodexExecutablePath, SettingsProblemCode.WrongFileName },
        { new RelaySettings { CodexExecutablePath = @"C:\Missing\codex.exe" }, SettingsField.CodexExecutablePath, SettingsProblemCode.NotFound },
        { new RelaySettings { AsrLabFolder = "asr" }, SettingsField.AsrLabFolder, SettingsProblemCode.NotFound },
        { new RelaySettings { AsrLabFolder = @"C:\Missing\asr" }, SettingsField.AsrLabFolder, SettingsProblemCode.NotFound },
        { new RelaySettings { AsrLabFolder = @"C:\Tools\empty" }, SettingsField.AsrLabFolder, SettingsProblemCode.MissingScripts },
        { new RelaySettings { AsrLabPort = 80 }, SettingsField.AsrLabPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { AsrLabPort = 47831 }, SettingsField.AsrLabPort, SettingsProblemCode.Forbidden },
        { new RelaySettings { AsrLabPort = 8765 }, SettingsField.AsrLabPort, SettingsProblemCode.Forbidden },
        { new RelaySettings { OwnedMappings = [Mapping(ConnectionMode.Private, 11443, "/livecue")] }, SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { new RelaySettings { OwnedMappings = [Mapping(ConnectionMode.Public, 10000, "/")] }, SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { new RelaySettings { OwnedMappings = [Mapping(ConnectionMode.Private, 0, "/")] }, SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { new RelaySettings { OwnedMappings = [Mapping(ConnectionMode.Private, 11443, "/", "http://localhost:47841")] }, SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { new RelaySettings { OwnedMappings = [Mapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:80")] }, SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Rule_cases(RelaySettings settings, SettingsField field, SettingsProblemCode code)
    {
        Assert.Equal(new SettingsProblem(field, code), Assert.Single(Validator().Validate(settings)));
    }

    [Fact]
    public void Valid_paths_ports_and_ledger_entries_pass()
    {
        var settings = new RelaySettings
        {
            RelayPort = 50123,
            ConnectionMode = ConnectionMode.Public,
            PublicHttpsPort = 8443,
            PublicPathPrefix = "/cue-2",
            CodexExecutablePath = Codex,
            AsrLabFolder = Lab,
            OwnedMappings =
            [
                Mapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:50123"),
                Mapping(ConnectionMode.Public, 8443, "/cue-2", "http://127.0.0.1:50123"),
            ],
        };

        Assert.Empty(Validator().Validate(settings));
    }

    [Theory]
    [InlineData(443)]
    [InlineData(8443)]
    [InlineData(10000)]
    public void The_public_port_may_be_any_port_funnel_can_use(int port)
    {
        Assert.Empty(Validator().Validate(new RelaySettings { PublicHttpsPort = port }));
    }

    [Fact]
    public void Structure_ignores_missing_paths()
    {
        var settings = new RelaySettings
        {
            CodexExecutablePath = @"C:\Missing\codex.exe",
            AsrLabFolder = @"C:\Missing\asr",
        };

        Assert.Empty(Validator().ValidateStructure(settings));
        Assert.Equal(
            [SettingsField.CodexExecutablePath, SettingsField.AsrLabFolder],
            Validator().Validate(settings).Select(problem => problem.Field));

        // What needs no file system is still checked when the file is loaded.
        Assert.Equal(
            new SettingsProblem(SettingsField.CodexExecutablePath, SettingsProblemCode.WrongFileName),
            Assert.Single(Validator().ValidateStructure(new RelaySettings { CodexExecutablePath = @"C:\Tools\other.exe" })));
    }

    [Fact]
    public void Problems_are_reported_in_field_order()
    {
        var settings = new RelaySettings { AsrLabPort = 80, RelayPort = 47831, SchemaVersion = 9 };

        Assert.Equal(
            [SettingsField.SchemaVersion, SettingsField.RelayPort, SettingsField.AsrLabPort],
            Validator().Validate(settings).Select(problem => problem.Field));
    }

    [Fact]
    public void The_foundations_store_keeps_valid_settings_in_memory_and_refuses_invalid_ones()
    {
        var files = Files();
        var store = new SettingsStore(@"C:\LiveCue-tests\data", files, new SettingsValidator(files));

        var missing = store.Load();
        store.Save(new RelaySettings { RelayPort = 50001 });
        var refused = Assert.Throws<SettingsValidationException>(() => store.Save(new RelaySettings { RelayPort = 47831 }));

        Assert.Equal(SettingsFileState.Missing, missing.State);
        Assert.True(missing.Settings.SameValues(RelaySettings.Default));
        Assert.Equal(SettingsFileState.Loaded, store.Load().State);
        Assert.Equal(50001, store.Load().Settings.RelayPort);
        Assert.Equal(new SettingsProblem(SettingsField.RelayPort, SettingsProblemCode.Forbidden), Assert.Single(refused.Problems));
        Assert.Equal(0, files.Writes);
    }

    [Fact]
    public void The_data_folder_is_the_remakes_own_and_never_the_previous_apps()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(local, "LiveCueRemake"), DataFolder.DefaultPath);
        Assert.Equal(DataFolder.DefaultPath, DataFolder.Resolve());
        Assert.Equal(DataFolder.DefaultPath, DataFolder.Resolve("  "));
        Assert.Equal(@"C:\LiveCue-tests\data", DataFolder.Resolve(@"C:\LiveCue-tests\data"));
    }

    [Fact]
    public void The_lab_folder_is_the_setting_else_the_bundled_folder_else_none()
    {
        var files = Files();
        var bundled = new FakeHostFileSystem { BaseDirectory = @"C:\App" }
            .AddFile(@"C:\App\asr\Start-ASR-Lab.ps1")
            .AddFile(@"C:\App\asr\Stop-ASR-Lab.ps1");

        Assert.Equal(Lab, LabFolderResolver.Resolve(new RelaySettings { AsrLabFolder = Lab }, files));
        Assert.Null(LabFolderResolver.Resolve(new RelaySettings { AsrLabFolder = @"C:\Tools\empty" }, files));
        Assert.Null(LabFolderResolver.Resolve(RelaySettings.Default, files));
        Assert.Equal(@"C:\App\asr", LabFolderResolver.Resolve(RelaySettings.Default, bundled));
    }

    private static OwnedMapping Mapping(ConnectionMode mode, int httpsPort, string path, string target = "http://127.0.0.1:47841") =>
        new(mode, httpsPort, path, target, new DateTimeOffset(2026, 10, 10, 8, 15, 30, TimeSpan.Zero));

    private static FakeHostFileSystem Files() => new FakeHostFileSystem()
        .AddFile(Codex)
        .AddFile(@"C:\Tools\other.exe")
        .AddFile(Lab + @"\Start-ASR-Lab.ps1")
        .AddFile(Lab + @"\Stop-ASR-Lab.ps1")
        .AddDirectory(@"C:\Tools\empty");

    private static SettingsValidator Validator() => new(Files());
}
