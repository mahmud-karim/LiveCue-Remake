using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Settings;

/// <summary>The rules of architecture 3.5, as the table of W-design 4.14 lists them (test plan 2.1).</summary>
public sealed class SettingsValidatorTests
{
    private const string Codex = @"C:\Tools\codex.exe";
    private const string Lab = @"C:\Tools\asr";

    [Fact]
    public void The_defaults_are_valid_and_are_the_values_of_the_architecture()
    {
        var settings = RelaySettings.Default;

        Assert.Empty(Validator().Validate(settings));
        Assert.Empty(Validator().ValidateStructure(settings));
        Assert.Equal(1, settings.SchemaVersion);
        Assert.Equal(47841, settings.RelayPort);
        Assert.Equal(ConnectionMode.Private, settings.ConnectionMode);
        Assert.Equal(11443, settings.PrivateHttpsPort);
        Assert.Equal(10000, settings.PublicHttpsPort);
        Assert.Equal("/livecue", settings.PublicPathPrefix);
        Assert.Null(settings.CodexExecutablePath);
        Assert.Null(settings.CodexContainmentCheckedVersion);
        Assert.Null(settings.AsrLabFolder);
        Assert.Equal(47842, settings.AsrLabPort);
        Assert.True(settings.HideFromScreenCapture);
        Assert.Empty(settings.OwnedMappings);
        Assert.True(settings.SameValues(new RelaySettings()));
    }

    /// <summary>One case per problem code of each field.</summary>
    public static TheoryData<RelaySettings, SettingsField, SettingsProblemCode> Rules => new()
    {
        { new RelaySettings { SchemaVersion = 2 }, SettingsField.SchemaVersion, SettingsProblemCode.UnsupportedVersion },
        { new RelaySettings { SchemaVersion = 0 }, SettingsField.SchemaVersion, SettingsProblemCode.UnsupportedVersion },

        { new RelaySettings { RelayPort = 1023 }, SettingsField.RelayPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { RelayPort = 65536 }, SettingsField.RelayPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { RelayPort = 0 }, SettingsField.RelayPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { RelayPort = 47831 }, SettingsField.RelayPort, SettingsProblemCode.Forbidden },
        { new RelaySettings { RelayPort = 47842 }, SettingsField.RelayPort, SettingsProblemCode.Duplicate },

        { new RelaySettings { ConnectionMode = (ConnectionMode)7 }, SettingsField.ConnectionMode, SettingsProblemCode.NotAllowed },

        { new RelaySettings { PrivateHttpsPort = 0 }, SettingsField.PrivateHttpsPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { PrivateHttpsPort = 65536 }, SettingsField.PrivateHttpsPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { PrivateHttpsPort = 8443 }, SettingsField.PrivateHttpsPort, SettingsProblemCode.NotAllowed },

        { new RelaySettings { PublicHttpsPort = 444 }, SettingsField.PublicHttpsPort, SettingsProblemCode.NotAllowed },
        { new RelaySettings { PublicHttpsPort = 0 }, SettingsField.PublicHttpsPort, SettingsProblemCode.NotAllowed },

        { new RelaySettings { PublicPathPrefix = "/" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "livecue" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "/LiveCue" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "/-livecue" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "/live/cue" }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = "/" + new string('a', 33) }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },
        { new RelaySettings { PublicPathPrefix = null! }, SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat },

        { new RelaySettings { CodexExecutablePath = "codex.exe" }, SettingsField.CodexExecutablePath, SettingsProblemCode.NotFound },
        { new RelaySettings { CodexExecutablePath = @"C:\Tools\other.exe" }, SettingsField.CodexExecutablePath, SettingsProblemCode.WrongFileName },
        { new RelaySettings { CodexExecutablePath = @"C:\Tools\codex.cmd" }, SettingsField.CodexExecutablePath, SettingsProblemCode.WrongFileName },
        { new RelaySettings { CodexExecutablePath = @"C:\Missing\codex.exe" }, SettingsField.CodexExecutablePath, SettingsProblemCode.NotFound },

        { new RelaySettings { CodexContainmentCheckedVersion = "" }, SettingsField.CodexContainmentCheckedVersion, SettingsProblemCode.BadFormat },
        { new RelaySettings { CodexContainmentCheckedVersion = new string('1', 65) }, SettingsField.CodexContainmentCheckedVersion, SettingsProblemCode.BadFormat },
        { new RelaySettings { CodexContainmentCheckedVersion = "codex \"1.2.3\"" }, SettingsField.CodexContainmentCheckedVersion, SettingsProblemCode.BadFormat },
        { new RelaySettings { CodexContainmentCheckedVersion = @"C:\codex" }, SettingsField.CodexContainmentCheckedVersion, SettingsProblemCode.BadFormat },

        { new RelaySettings { AsrLabFolder = "asr" }, SettingsField.AsrLabFolder, SettingsProblemCode.NotAllowed },
        { new RelaySettings { AsrLabFolder = @"N:\asr" }, SettingsField.AsrLabFolder, SettingsProblemCode.NotAllowed },
        { new RelaySettings { AsrLabFolder = @"C:\Missing\asr" }, SettingsField.AsrLabFolder, SettingsProblemCode.NotFound },
        { new RelaySettings { AsrLabFolder = @"C:\Tools\empty" }, SettingsField.AsrLabFolder, SettingsProblemCode.MissingScripts },
        { new RelaySettings { AsrLabFolder = @"C:\Tools\half" }, SettingsField.AsrLabFolder, SettingsProblemCode.MissingScripts },

        { new RelaySettings { AsrLabPort = 80 }, SettingsField.AsrLabPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { AsrLabPort = 65536 }, SettingsField.AsrLabPort, SettingsProblemCode.OutOfRange },
        { new RelaySettings { AsrLabPort = 47831 }, SettingsField.AsrLabPort, SettingsProblemCode.Forbidden },
        { new RelaySettings { AsrLabPort = 8765 }, SettingsField.AsrLabPort, SettingsProblemCode.Forbidden },

        { new RelaySettings { OwnedMappings = null! }, SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 11443, "/livecue")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Public, 10000, "/")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping((ConnectionMode)7, 11443, "/")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 0, "/")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 65536, "/")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 11443, "/", "http://localhost:47841")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 11443, "/", "https://127.0.0.1:47841")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:80")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:65536")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:047841")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841/")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
        { Ledger(Mapping(ConnectionMode.Private, 11443, "/"), Mapping(ConnectionMode.Private, 0, "/")), SettingsField.OwnedMappings, SettingsProblemCode.BadFormat },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Rule_cases(RelaySettings settings, SettingsField field, SettingsProblemCode code)
    {
        Assert.Equal(new SettingsProblem(field, code), Assert.Single(Validator().Validate(settings)));
    }

    [Fact]
    public void A_private_port_equal_to_the_public_port_is_a_duplicate()
    {
        // The public port must be a Funnel port and the private port must not be one, so the two can
        // only be equal when the public port is itself wrong. Both problems are reported.
        var problems = Validator().Validate(new RelaySettings { PrivateHttpsPort = 444, PublicHttpsPort = 444 });

        Assert.Equal(
            [
                new SettingsProblem(SettingsField.PrivateHttpsPort, SettingsProblemCode.Duplicate),
                new SettingsProblem(SettingsField.PublicHttpsPort, SettingsProblemCode.NotAllowed),
            ],
            problems);
    }

    [Fact]
    public void Forbidden_ports()
    {
        var validator = Validator();

        Assert.Equal(
            new SettingsProblem(SettingsField.RelayPort, SettingsProblemCode.Forbidden),
            Assert.Single(validator.Validate(new RelaySettings { RelayPort = 47831 })));
        Assert.Equal(
            new SettingsProblem(SettingsField.AsrLabPort, SettingsProblemCode.Forbidden),
            Assert.Single(validator.Validate(new RelaySettings { AsrLabPort = 47831 })));
        Assert.Equal(
            new SettingsProblem(SettingsField.AsrLabPort, SettingsProblemCode.Forbidden),
            Assert.Single(validator.Validate(new RelaySettings { AsrLabPort = 8765 })));

        // 8765 is the lab's port inside its container; it is an ordinary port for the relay.
        Assert.Empty(validator.Validate(new RelaySettings { RelayPort = 8765 }));

        // The same rules hold when the file is loaded.
        Assert.Contains(
            new SettingsProblem(SettingsField.RelayPort, SettingsProblemCode.Forbidden),
            validator.ValidateStructure(new RelaySettings { RelayPort = 47831 }));
        Assert.Equal(47831, RelaySettings.ForbiddenPort);
    }

    [Theory]
    [InlineData(443)]
    [InlineData(8443)]
    [InlineData(10000)]
    public void Private_port_may_not_be_a_funnel_port(int port)
    {
        // A public port that differs, so that the only problem is the private port itself.
        var settings = new RelaySettings { PrivateHttpsPort = port, PublicHttpsPort = port == 443 ? 8443 : 443 };
        var expected = new SettingsProblem(SettingsField.PrivateHttpsPort, SettingsProblemCode.NotAllowed);

        Assert.Equal(expected, Assert.Single(Validator().Validate(settings)));
        Assert.Equal(expected, Assert.Single(Validator().ValidateStructure(settings)));

        // With the public port on the same number it is still "not allowed", not "duplicate".
        Assert.Contains(expected, Validator().Validate(new RelaySettings { PrivateHttpsPort = port, PublicHttpsPort = port }));
    }

    [Theory]
    [InlineData(443)]
    [InlineData(8443)]
    [InlineData(10000)]
    public void The_public_port_may_be_any_port_funnel_can_use(int port)
    {
        Assert.Empty(Validator().Validate(new RelaySettings { PublicHttpsPort = port }));
    }

    [Theory]
    [InlineData(@"\\server\share\asr")]
    [InlineData(@"\\?\C:\Tools\asr")]
    [InlineData(@"\\.\C:\Tools\asr")]
    [InlineData(@"\\?\UNC\server\share\asr")]
    [InlineData("asr")]
    [InlineData(@"Tools\asr")]
    [InlineData(@"\Tools\asr")]
    [InlineData(@"C:asr")]
    [InlineData("C:")]
    [InlineData("C:/Tools/asr")]
    [InlineData("")]
    public void Lab_folder_path_forms(string folder)
    {
        var expected = new SettingsProblem(SettingsField.AsrLabFolder, SettingsProblemCode.NotAllowed);
        var files = Files();
        var probes = 0;
        files.FixedLocalDrive = _ =>
        {
            probes++;
            return true;
        };
        var validator = new SettingsValidator(files);
        var settings = new RelaySettings { AsrLabFolder = folder };

        // The path form needs no file system: it is refused when the file is loaded, too.
        Assert.Equal(expected, Assert.Single(validator.ValidateStructure(settings)));
        Assert.Equal(expected, Assert.Single(validator.Validate(settings)));
        Assert.Equal(0, probes);
    }

    [Fact]
    public void A_lab_folder_on_a_drive_that_is_not_fixed_is_not_allowed_when_saving()
    {
        var files = Files();
        files.FixedLocalDrive = path => !path.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase);
        var validator = new SettingsValidator(files);
        var settings = new RelaySettings { AsrLabFolder = Lab };

        // A network or removable drive has a drive letter, so only the file system can tell.
        Assert.Equal(
            new SettingsProblem(SettingsField.AsrLabFolder, SettingsProblemCode.NotAllowed),
            Assert.Single(validator.Validate(settings)));
        Assert.Empty(validator.ValidateStructure(settings));
    }

    public static TheoryData<string> LineBreaks => new() { "\n", "\r\n", "\u2028" };

    [Theory]
    [MemberData(nameof(LineBreaks))]
    public void Shapes_are_whole_text_matches(string lineBreak)
    {
        var validator = Validator();

        Assert.Equal(
            new SettingsProblem(SettingsField.PublicPathPrefix, SettingsProblemCode.BadFormat),
            Assert.Single(validator.ValidateStructure(new RelaySettings { PublicPathPrefix = "/livecue" + lineBreak })));
        Assert.Equal(
            new SettingsProblem(SettingsField.CodexContainmentCheckedVersion, SettingsProblemCode.BadFormat),
            Assert.Single(validator.ValidateStructure(new RelaySettings { CodexContainmentCheckedVersion = "codex-cli 1.2.3" + lineBreak })));

        // The ledger: the path of a public entry, the path of a private entry and the target.
        Assert.False(validator.IsValidLedgerEntry(Mapping(ConnectionMode.Public, 10000, "/livecue" + lineBreak)));
        Assert.False(validator.IsValidLedgerEntry(Mapping(ConnectionMode.Private, 11443, "/" + lineBreak)));
        Assert.False(validator.IsValidLedgerEntry(Mapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841" + lineBreak)));
        Assert.Equal(
            new SettingsProblem(SettingsField.OwnedMappings, SettingsProblemCode.BadFormat),
            Assert.Single(validator.ValidateStructure(Ledger(Mapping(ConnectionMode.Public, 10000, "/livecue" + lineBreak)))));

        // The same texts without the line break pass.
        Assert.Empty(validator.ValidateStructure(new RelaySettings
        {
            PublicPathPrefix = "/livecue",
            CodexContainmentCheckedVersion = "codex-cli 1.2.3",
            OwnedMappings = [Mapping(ConnectionMode.Public, 10000, "/livecue"), Mapping(ConnectionMode.Private, 11443, "/")],
        }));
    }

    [Theory]
    [InlineData("codex-cli 0.46.0")]
    [InlineData("1")]
    [InlineData("codex_cli 1.2.3-beta.4+build.5")]
    public void A_version_text_of_the_fixed_shape_is_accepted(string version)
    {
        Assert.Empty(Validator().Validate(new RelaySettings { CodexContainmentCheckedVersion = version }));
        Assert.Empty(Validator().Validate(new RelaySettings { CodexContainmentCheckedVersion = new string('a', 64) }));
    }

    [Fact]
    public void Valid_paths_ports_and_ledger_entries_pass()
    {
        var settings = new RelaySettings
        {
            RelayPort = 50123,
            ConnectionMode = ConnectionMode.Public,
            PrivateHttpsPort = 1,
            PublicHttpsPort = 8443,
            PublicPathPrefix = "/cue-2",
            CodexExecutablePath = @"C:\Tools\CODEX.EXE",
            CodexContainmentCheckedVersion = "codex-cli 1.2.3",
            AsrLabFolder = Lab,
            AsrLabPort = 65535,
            HideFromScreenCapture = false,
            OwnedMappings =
            [
                Mapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:50123"),
                Mapping(ConnectionMode.Public, 8443, "/cue-2", "http://127.0.0.1:50123"),
                Mapping(ConnectionMode.Private, 65535, "/", "http://127.0.0.1:1024"),
            ],
        };

        Assert.Empty(Validator().Validate(settings));
        Assert.All(settings.OwnedMappings, entry => Assert.True(Validator().IsValidLedgerEntry(entry)));
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
            [
                new SettingsProblem(SettingsField.CodexExecutablePath, SettingsProblemCode.NotFound),
                new SettingsProblem(SettingsField.AsrLabFolder, SettingsProblemCode.NotFound),
            ],
            Validator().Validate(settings));

        // A folder without its scripts is a file-system question as well.
        Assert.Empty(Validator().ValidateStructure(new RelaySettings { AsrLabFolder = @"C:\Tools\empty" }));

        // The structure check asks the file system nothing at all.
        var silent = new FakeHostFileSystem { FixedLocalDrive = _ => throw new InvalidOperationException("Not asked.") };
        Assert.Empty(new SettingsValidator(silent).ValidateStructure(settings));

        // What needs no file system is still checked when the file is loaded.
        Assert.Equal(
            new SettingsProblem(SettingsField.CodexExecutablePath, SettingsProblemCode.WrongFileName),
            Assert.Single(Validator().ValidateStructure(new RelaySettings { CodexExecutablePath = @"C:\Tools\other.exe" })));
        Assert.Equal(
            new SettingsProblem(SettingsField.CodexExecutablePath, SettingsProblemCode.NotFound),
            Assert.Single(Validator().ValidateStructure(new RelaySettings { CodexExecutablePath = @"Tools\codex.exe" })));
    }

    [Fact]
    public void Problems_are_reported_in_field_order()
    {
        var settings = new RelaySettings
        {
            OwnedMappings = [Mapping(ConnectionMode.Private, 0, "/")],
            AsrLabPort = 80,
            AsrLabFolder = "asr",
            CodexContainmentCheckedVersion = "",
            CodexExecutablePath = "codex.exe",
            PublicPathPrefix = "/",
            PublicHttpsPort = 1,
            PrivateHttpsPort = 0,
            ConnectionMode = (ConnectionMode)9,
            RelayPort = 47831,
            SchemaVersion = 9,
        };

        Assert.Equal(
            [
                SettingsField.SchemaVersion, SettingsField.RelayPort, SettingsField.ConnectionMode,
                SettingsField.PrivateHttpsPort, SettingsField.PublicHttpsPort, SettingsField.PublicPathPrefix,
                SettingsField.CodexExecutablePath, SettingsField.CodexContainmentCheckedVersion,
                SettingsField.AsrLabFolder, SettingsField.AsrLabPort, SettingsField.OwnedMappings,
            ],
            Validator().Validate(settings).Select(problem => problem.Field));
    }

    private static OwnedMapping Mapping(ConnectionMode mode, int httpsPort, string path, string target = "http://127.0.0.1:47841") =>
        new(mode, httpsPort, path, target, new DateTimeOffset(2026, 10, 10, 8, 15, 30, TimeSpan.Zero));

    private static RelaySettings Ledger(params OwnedMapping[] entries) => new() { OwnedMappings = entries };

    /// <summary>Drive C is a fixed local drive; drive N is a network drive.</summary>
    private static FakeHostFileSystem Files() => new FakeHostFileSystem
    {
        FixedLocalDrive = path => path.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase),
    }
        .AddFile(Codex)
        .AddFile(@"C:\Tools\other.exe")
        .AddFile(@"C:\Tools\codex.cmd")
        .AddFile(Lab + @"\Start-ASR-Lab.ps1")
        .AddFile(Lab + @"\Stop-ASR-Lab.ps1")
        .AddFile(@"C:\Tools\half\Start-ASR-Lab.ps1")
        .AddFile(@"N:\asr\Start-ASR-Lab.ps1")
        .AddFile(@"N:\asr\Stop-ASR-Lab.ps1")
        .AddDirectory(@"C:\Tools\empty");

    private static SettingsValidator Validator() => new(Files());
}
