using LiveCue.Relay;

namespace LiveCue.Hosting.Tests;

/// <summary>settings.json: defaults, the rules of architecture 3.5, and how the file is read and written.</summary>
public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LiveCue-hosting-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void The_defaults_are_valid_and_use_port_47841()
    {
        var settings = new HostSettings();

        Assert.Empty(settings.Validate());
        Assert.Equal(47841, settings.RelayPort);
        Assert.Equal(ConnectionMode.Private, settings.ConnectionMode);
        Assert.Equal(11443, settings.PrivateHttpsPort);
        Assert.Equal(10000, settings.PublicHttpsPort);
        Assert.Equal("/livecue", settings.PublicPathPrefix);
        Assert.Equal(47842, settings.AsrLabPort);
    }

    public static TheoryData<HostSettings> InvalidSettings => new()
    {
        new HostSettings { SchemaVersion = 2 },
        new HostSettings { RelayPort = 47831 },
        new HostSettings { RelayPort = 1023 },
        new HostSettings { RelayPort = 65536 },
        new HostSettings { RelayPort = 0 },
        new HostSettings { RelayPort = 47842 },                    // equal to the lab port
        new HostSettings { ConnectionMode = (ConnectionMode)7 },
        new HostSettings { PrivateHttpsPort = 0 },
        new HostSettings { PrivateHttpsPort = 10000 },             // equal to the public port
        new HostSettings { PublicHttpsPort = 444 },
        new HostSettings { PublicPathPrefix = "/" },
        new HostSettings { PublicPathPrefix = "livecue" },
        new HostSettings { PublicPathPrefix = "/LiveCue" },
        new HostSettings { PublicPathPrefix = "/-livecue" },
        new HostSettings { PublicPathPrefix = "/" + new string('a', 33) },
        new HostSettings { CodexExecutablePath = "codex.exe" },    // not absolute
        new HostSettings { CodexExecutablePath = @"C:\Tools\other.exe" },
        new HostSettings { AsrLabFolder = "asr" },
        new HostSettings { AsrLabPort = 47831 },
        new HostSettings { AsrLabPort = 8765 },
        new HostSettings { AsrLabPort = 80 },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void Settings_that_break_a_rule_are_reported_and_cannot_be_saved(HostSettings settings)
    {
        Assert.NotEmpty(settings.Validate());
        Assert.Throws<ArgumentException>(() => new FileSettingsStore(_folder).Save(settings));
        Assert.Throws<ArgumentException>(() => new InMemorySettingsStore().Save(settings));
        Assert.False(Directory.Exists(_folder), "A refused save must not create anything.");
    }

    [Theory]
    [InlineData(443)]
    [InlineData(8443)]
    [InlineData(10000)]
    public void The_public_port_may_be_any_port_funnel_can_use(int port)
    {
        Assert.Empty(new HostSettings { PublicHttpsPort = port }.Validate());
    }

    [Fact]
    public void A_missing_file_means_defaults_and_loading_creates_nothing()
    {
        var result = new FileSettingsStore(_folder).Load();

        Assert.Equal(SettingsSource.Defaults, result.Source);
        Assert.Equal(new HostSettings().RelayPort, result.Settings.RelayPort);
        Assert.Empty(result.Problems);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void Saved_settings_are_the_documented_json_and_load_back()
    {
        var store = new FileSettingsStore(_folder);

        store.Save(new HostSettings());

        var expected = """
            {
              "schemaVersion": 1,
              "relayPort": 47841,
              "connectionMode": "private",
              "privateHttpsPort": 11443,
              "publicHttpsPort": 10000,
              "publicPathPrefix": "/livecue",
              "codexExecutablePath": null,
              "asrLabFolder": null,
              "asrLabPort": 47842,
              "ownedMappings": []
            }
            """;
        Assert.Equal(Normalise(expected), Normalise(File.ReadAllText(store.FilePath)));
        Assert.False(File.Exists(store.FilePath + ".pending"), "The pending file is replaced into place, not left behind.");
        Assert.Equal(SettingsSource.File, store.Load().Source);
    }

    [Fact]
    public void Changed_settings_round_trip()
    {
        var store = new FileSettingsStore(_folder);
        var changed = new HostSettings
        {
            RelayPort = 50123,
            ConnectionMode = ConnectionMode.Public,
            PublicHttpsPort = 8443,
            PublicPathPrefix = "/cue-2",
            CodexExecutablePath = @"C:\Tools\codex.exe",
            OwnedMappings = [new OwnedMapping("private", 11443, "/", "http://127.0.0.1:50123", new DateTimeOffset(2026, 10, 10, 8, 15, 30, TimeSpan.Zero))],
        };

        store.Save(changed);
        var loaded = store.Load();

        Assert.Equal(SettingsSource.File, loaded.Source);
        Assert.Equal(50123, loaded.Settings.RelayPort);
        Assert.Equal(ConnectionMode.Public, loaded.Settings.ConnectionMode);
        Assert.Equal(8443, loaded.Settings.PublicHttpsPort);
        Assert.Equal("/cue-2", loaded.Settings.PublicPathPrefix);
        Assert.Equal(@"C:\Tools\codex.exe", loaded.Settings.CodexExecutablePath);
        Assert.Equal(changed.OwnedMappings[0], Assert.Single(loaded.Settings.OwnedMappings));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"schemaVersion":1,"relayPort":47831}""")]
    [InlineData("""{"schemaVersion":1,"connectionMode":"tailnet"}""")]
    [InlineData("""{"schemaVersion":1,"relayPort":"47841"}""")]
    public void A_file_that_fails_validation_is_left_untouched_and_defaults_apply(string content)
    {
        var store = new FileSettingsStore(_folder);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.FilePath, content);

        var result = store.Load();

        Assert.Equal(SettingsSource.DefaultsBecauseFileIsInvalid, result.Source);
        Assert.NotEmpty(result.Problems);
        Assert.Equal(47841, result.Settings.RelayPort);
        Assert.Equal(content, File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void The_in_memory_store_keeps_what_was_saved_and_touches_no_file()
    {
        var store = new InMemorySettingsStore();

        store.Save(new HostSettings { RelayPort = 50001 });

        Assert.Equal(50001, store.Load().Settings.RelayPort);
    }

    [Fact]
    public void The_data_folder_is_the_remakes_own_and_never_the_previous_apps()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(local, "LiveCueRemake"), DataFolder.DefaultPath);
        Assert.Equal(DataFolder.DefaultPath, DataFolder.Resolve());
        Assert.Equal(DataFolder.DefaultPath, DataFolder.Resolve("  "));
        Assert.Equal(Path.GetFullPath(_folder), DataFolder.Resolve(_folder));
    }

    private static string Normalise(string text) => text.ReplaceLineEndings("\n").Trim();
}
