using System.Text;
using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Hosting.Tests.Settings;

/// <summary>
/// <c>settings.json</c>: its format, its load rules and its atomic save (architecture 3.5, W-design 4.14,
/// test plan 2.1). The file system is the fake one, except in the last test.
/// </summary>
public sealed class SettingsStoreTests
{
    private const string DataDirectory = @"C:\LiveCue-tests\data";
    private const string FilePath = DataDirectory + @"\settings.json";
    private const string InvalidCopyPath = DataDirectory + @"\settings.json.invalid";

    /// <summary>The example of architecture 3.5, as the store writes it.</summary>
    private const string DefaultText =
        "{\n" +
        "  \"schemaVersion\": 1,\n" +
        "  \"relayPort\": 47841,\n" +
        "  \"connectionMode\": \"private\",\n" +
        "  \"privateHttpsPort\": 11443,\n" +
        "  \"publicHttpsPort\": 10000,\n" +
        "  \"publicPathPrefix\": \"/livecue\",\n" +
        "  \"codexExecutablePath\": null,\n" +
        "  \"codexContainmentCheckedVersion\": null,\n" +
        "  \"asrLabFolder\": null,\n" +
        "  \"asrLabPort\": 47842,\n" +
        "  \"hideFromScreenCapture\": true,\n" +
        "  \"ownedMappings\": []\n" +
        "}\n";

    private static readonly DateTimeOffset Created = new(2026, 10, 10, 8, 15, 30, TimeSpan.Zero);

    private readonly FakeHostFileSystem _files = new FakeHostFileSystem()
        .AddFile(@"C:\Tools\codex.exe")
        .AddFile(@"C:\Tools\asr\Start-ASR-Lab.ps1")
        .AddFile(@"C:\Tools\asr\Stop-ASR-Lab.ps1");

    [Fact]
    public void A_missing_file_is_the_defaults()
    {
        var result = Store().Load();

        Assert.Equal(SettingsFileState.Missing, result.State);
        Assert.Empty(result.Problems);
        Assert.True(result.Settings.SameValues(RelaySettings.Default));
        Assert.Equal(47841, result.Settings.RelayPort);
        Assert.Equal(ConnectionMode.Private, result.Settings.ConnectionMode);

        // Loading writes nothing and creates nothing.
        Assert.Equal(0, _files.Writes);
        Assert.False(_files.DirectoryExists(DataDirectory));
    }

    [Fact]
    public void An_invalid_file_is_left_untouched()
    {
        var valid = Entry(ConnectionMode.Private, 11443, "/");
        var alsoValid = Entry(ConnectionMode.Public, 10000, "/livecue");
        var text = FileText(
            relayPort: "47831",
            ownedMappings: Array(
                EntryText("private", 11443, "/"),
                EntryText("private", 0, "/"),
                EntryText("tailnet", 11443, "/"),
                EntryText("public", 10000, "/livecue"),
                EntryText("public", 10000, "/livecue\\n"),
                EntryText("private", 11443, "/", createdAt: "yesterday")));
        var bytes = Put(text);

        var result = Store().Load();

        Assert.Equal(SettingsFileState.Invalid, result.State);
        Assert.Equal(
            [
                new SettingsProblem(SettingsField.RelayPort, SettingsProblemCode.Forbidden),
                new SettingsProblem(SettingsField.OwnedMappings, SettingsProblemCode.BadFormat),
            ],
            result.Problems);

        // The defaults apply, with the entries of the file's ledger that are valid by themselves, in file order.
        Assert.True(result.Settings.SameValues(RelaySettings.Default with { OwnedMappings = [valid, alsoValid] }));
        Assert.Equal(47841, result.Settings.RelayPort);

        // Byte for byte what it was, and nothing was written beside it.
        Assert.Equal(bytes, _files.ReadAllBytes(FilePath));
        Assert.Equal(0, _files.Writes);
        Assert.Equal([FilePath], DataFiles());
    }

    public static TheoryData<string, string> InvalidValues => new()
    {
        { "schemaVersion", "2" },
        { "schemaVersion", "1.5" },
        { "relayPort", "80" },
        { "relayPort", "99999999999" },
        { "relayPort", "47841.5" },
        { "connectionMode", "\"tailnet\"" },
        { "connectionMode", "\"Private\"" },
        { "privateHttpsPort", "10000" },
        { "publicHttpsPort", "444" },
        { "publicPathPrefix", "\"/\"" },
        { "publicPathPrefix", "\"/livecue\\n\"" },
        { "codexExecutablePath", "\"C:\\\\Tools\\\\codex.cmd\"" },
        { "codexContainmentCheckedVersion", "\"1.2.3\\n\"" },
        { "asrLabFolder", "\"\\\\\\\\server\\\\share\\\\asr\"" },
        { "asrLabPort", "8765" },
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void A_value_of_the_right_type_that_breaks_a_rule_makes_the_file_invalid(string member, string json)
    {
        var bytes = Put(FileText().Replace(MemberLine(member), $"\"{member}\": {json}", StringComparison.Ordinal));

        var result = Store().Load();

        Assert.Equal(SettingsFileState.Invalid, result.State);
        Assert.Equal(member, FieldName(Assert.Single(result.Problems).Field));
        Assert.True(result.Settings.SameValues(RelaySettings.Default));
        Assert.Equal(bytes, _files.ReadAllBytes(FilePath));
    }

    public static TheoryData<string> UnreadableFiles => new()
    {
        "",
        "not json",
        "[]",
        "\"settings\"",
        "null",
        "{}",
        DefaultText + "{}",
        DefaultText.Replace("47841", "\"47841\"", StringComparison.Ordinal),
        DefaultText.Replace("\"private\"", "0", StringComparison.Ordinal),
        DefaultText.Replace("\"private\"", "null", StringComparison.Ordinal),
        DefaultText.Replace("\"/livecue\"", "null", StringComparison.Ordinal),
        DefaultText.Replace("\"codexExecutablePath\": null", "\"codexExecutablePath\": 5", StringComparison.Ordinal),
        DefaultText.Replace("\"asrLabFolder\": null", "\"asrLabFolder\": false", StringComparison.Ordinal),
        DefaultText.Replace("\"codexContainmentCheckedVersion\": null", "\"codexContainmentCheckedVersion\": 1.2", StringComparison.Ordinal),
        DefaultText.Replace("true", "\"yes\"", StringComparison.Ordinal),
        DefaultText.Replace("true", "1", StringComparison.Ordinal),
        DefaultText.Replace("[]", "{}", StringComparison.Ordinal),
        DefaultText.Replace("[]", "null", StringComparison.Ordinal),
        DefaultText.Replace("[]", "[5]", StringComparison.Ordinal),
        DefaultText.Replace("[]", "[{\"mode\":\"private\",\"httpsPort\":\"11443\",\"path\":\"/\",\"target\":\"http://127.0.0.1:47841\",\"createdAt\":\"2026-10-10T08:15:30Z\"}]", StringComparison.Ordinal),
        DefaultText.Replace("[]", "[{\"mode\":\"private\",\"httpsPort\":11443,\"path\":\"/\",\"target\":\"http://127.0.0.1:47841\",\"createdAt\":1}]", StringComparison.Ordinal),
        DefaultText.Replace("[]", "[{\"mode\":\"private\",\"httpsPort\":11443,\"path\":\"/\",\"createdAt\":\"2026-10-10T08:15:30Z\"}]", StringComparison.Ordinal),
        DefaultText.Replace("  \"relayPort\": 47841,\n", string.Empty, StringComparison.Ordinal),
        DefaultText.Replace("  \"asrLabFolder\": null,\n", string.Empty, StringComparison.Ordinal),
        DefaultText.Replace("  \"ownedMappings\": []\n", "  \"extra\": 1\n", StringComparison.Ordinal),
        DefaultText.Replace("\"ownedMappings\": []", "\"ownedMappings\": [],", StringComparison.Ordinal),
        DefaultText.Replace("{\n", "{\n  // a comment\n", StringComparison.Ordinal),
        DefaultText.Replace("[]", "[[[[[[[[[[1]]]]]]]]]]", StringComparison.Ordinal),
    };

    [Theory]
    [MemberData(nameof(UnreadableFiles))]
    public void An_unreadable_file_is_left_untouched(string text)
    {
        var bytes = Put(text);

        var result = Store().Load();

        Assert.Equal(SettingsFileState.Unreadable, result.State);
        Assert.Empty(result.Problems);
        Assert.True(result.Settings.SameValues(RelaySettings.Default));
        Assert.Equal(bytes, _files.ReadAllBytes(FilePath));
        Assert.Equal(0, _files.Writes);
        Assert.Equal([FilePath], DataFiles());
    }

    [Fact]
    public void An_oversized_file_and_bytes_that_are_not_text_are_unreadable()
    {
        // W-05: 64 KiB is read; one byte more is not, although the text is a valid settings file.
        var padding = new string(' ', 65_536 - DefaultText.Length);
        Put(DefaultText + padding);
        Assert.Equal(SettingsFileState.Loaded, Store().Load().State);

        var oversized = Put(DefaultText + padding + " ");
        Assert.Equal(65_537, oversized.Length);
        Assert.Equal(SettingsFileState.Unreadable, Store().Load().State);
        Assert.Equal(oversized, _files.ReadAllBytes(FilePath));

        _files.AddFile(FilePath, [0x7B, 0xFF, 0xFE, 0x7D]);
        Assert.Equal(SettingsFileState.Unreadable, Store().Load().State);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_unreadable_and_a_load_never_throws()
    {
        var store = new SettingsStore(DataDirectory, new ThrowingFiles(), new SettingsValidator(_files));

        var result = store.Load();

        Assert.Equal(SettingsFileState.Unreadable, result.State);
        Assert.True(result.Settings.SameValues(RelaySettings.Default));
    }

    [Fact]
    public void Unknown_members_are_ignored()
    {
        Put(FileText(relayPort: "50123", ownedMappings: Array(
                "{\"mode\":\"private\",\"httpsPort\":11443,\"path\":\"/\",\"target\":\"http://127.0.0.1:50123\",\"createdAt\":\"2026-10-10T08:15:30Z\",\"note\":\"mine\"}"))
            .Replace("{\n", "{\n  \"theme\": \"dark\",\n  \"window\": { \"left\": 10, \"tabs\": [1, 2] },\n", StringComparison.Ordinal));
        var store = Store();

        var result = store.Load();

        Assert.Equal(SettingsFileState.Loaded, result.State);
        Assert.Empty(result.Problems);
        Assert.Equal(50123, result.Settings.RelayPort);
        Assert.Equal([Entry(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:50123")], result.Settings.OwnedMappings);

        // They are not written back.
        store.Save(result.Settings);
        var written = Text(_files.ReadAllBytes(FilePath));
        Assert.DoesNotContain("theme", written, StringComparison.Ordinal);
        Assert.DoesNotContain("window", written, StringComparison.Ordinal);
        Assert.DoesNotContain("note", written, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_members_the_review_added_may_be_absent()
    {
        Put(DefaultText
            .Replace("  \"codexContainmentCheckedVersion\": null,\n", string.Empty, StringComparison.Ordinal)
            .Replace("  \"hideFromScreenCapture\": true,\n", string.Empty, StringComparison.Ordinal));

        var result = Store().Load();

        Assert.Equal(SettingsFileState.Loaded, result.State);
        Assert.Null(result.Settings.CodexContainmentCheckedVersion);
        Assert.True(result.Settings.HideFromScreenCapture);

        // Present, they are read.
        Put(DefaultText
            .Replace("\"codexContainmentCheckedVersion\": null", "\"codexContainmentCheckedVersion\": \"codex-cli 1.2.3\"", StringComparison.Ordinal)
            .Replace("true", "false", StringComparison.Ordinal));
        var present = Store().Load();
        Assert.Equal(SettingsFileState.Loaded, present.State);
        Assert.Equal("codex-cli 1.2.3", present.Settings.CodexContainmentCheckedVersion);
        Assert.False(present.Settings.HideFromScreenCapture);
    }

    [Fact]
    public void A_byte_order_mark_and_a_folder_that_was_deleted_later_do_not_spoil_the_file()
    {
        // The structure check asks the file system nothing: the paths of the file need not exist any more.
        var text = FileText(codexExecutablePath: "\"C:\\\\Gone\\\\codex.exe\"", asrLabFolder: "\"C:\\\\Gone\\\\asr\"");
        _files.AddFile(FilePath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)]);

        var result = Store().Load();

        Assert.Equal(SettingsFileState.Loaded, result.State);
        Assert.Equal(@"C:\Gone\codex.exe", result.Settings.CodexExecutablePath);
        Assert.Equal(@"C:\Gone\asr", result.Settings.AsrLabFolder);
    }

    [Fact]
    public void Save_refuses_invalid_settings()
    {
        var store = Store();
        store.Save(new RelaySettings { RelayPort = 50001 });
        var saved = _files.ReadAllBytes(FilePath);

        var forbidden = Assert.Throws<SettingsValidationException>(() => store.Save(new RelaySettings { RelayPort = 47831 }));
        var missing = Assert.Throws<SettingsValidationException>(
            () => store.Save(new RelaySettings { CodexExecutablePath = @"C:\Gone\codex.exe", AsrLabFolder = @"C:\Gone\asr" }));

        Assert.Equal(new SettingsProblem(SettingsField.RelayPort, SettingsProblemCode.Forbidden), Assert.Single(forbidden.Problems));

        // A save uses every rule, the ones that ask the file system included.
        Assert.Equal(
            [
                new SettingsProblem(SettingsField.CodexExecutablePath, SettingsProblemCode.NotFound),
                new SettingsProblem(SettingsField.AsrLabFolder, SettingsProblemCode.NotFound),
            ],
            missing.Problems);
        Assert.Equal(saved, _files.ReadAllBytes(FilePath));
        Assert.Equal(1, _files.Writes);
        Assert.Equal(50001, store.Load().Settings.RelayPort);
    }

    [Fact]
    public void A_failed_write_keeps_the_old_file()
    {
        var store = Store();
        store.Save(new RelaySettings { RelayPort = 50001 });
        var saved = _files.ReadAllBytes(FilePath);
        _files.WritesFail = true;

        Assert.Throws<IOException>(() => store.Save(new RelaySettings { RelayPort = 50002 }));

        Assert.Equal(saved, _files.ReadAllBytes(FilePath));
        Assert.Equal(50001, store.Load().Settings.RelayPort);
        Assert.Equal([FilePath], DataFiles());
    }

    [Fact]
    public void A_file_system_that_refuses_the_write_is_an_io_exception_with_a_fixed_message()
    {
        var store = new SettingsStore(DataDirectory, new ThrowingFiles { FileIsThere = false }, new SettingsValidator(_files));

        var exception = Assert.Throws<IOException>(() => store.Save(RelaySettings.Default));

        Assert.Equal("The settings could not be saved.", exception.Message);
    }

    [Fact]
    public void The_file_format_is_exact_and_round_trips()
    {
        var store = Store();

        store.Save(RelaySettings.Default);
        Assert.Equal(DefaultText, Text(_files.ReadAllBytes(FilePath)));
        Assert.True(_files.DirectoryExists(DataDirectory));
        Assert.Equal(SettingsFileState.Loaded, store.Load().State);
        Assert.True(store.Load().Settings.SameValues(RelaySettings.Default));

        var settings = new RelaySettings
        {
            RelayPort = 50123,
            ConnectionMode = ConnectionMode.Public,
            PrivateHttpsPort = 12443,
            PublicHttpsPort = 8443,
            PublicPathPrefix = "/cue-2",
            CodexExecutablePath = @"C:\Tools\codex.exe",
            CodexContainmentCheckedVersion = "codex-cli 1.2.3+build",
            AsrLabFolder = @"C:\Tools\asr",
            AsrLabPort = 50124,
            HideFromScreenCapture = false,
            OwnedMappings =
            [
                Entry(ConnectionMode.Private, 12443, "/", "http://127.0.0.1:50123"),

                // A time with a fraction and an offset: the file holds UTC to the second.
                new OwnedMapping(
                    ConnectionMode.Public, 8443, "/cue-2", "http://127.0.0.1:50123",
                    new DateTimeOffset(2026, 10, 10, 10, 15, 31, 750, TimeSpan.FromHours(2))),
            ],
        };

        store.Save(settings);

        Assert.Equal(
            "{\n" +
            "  \"schemaVersion\": 1,\n" +
            "  \"relayPort\": 50123,\n" +
            "  \"connectionMode\": \"public\",\n" +
            "  \"privateHttpsPort\": 12443,\n" +
            "  \"publicHttpsPort\": 8443,\n" +
            "  \"publicPathPrefix\": \"/cue-2\",\n" +
            "  \"codexExecutablePath\": \"C:\\\\Tools\\\\codex.exe\",\n" +
            "  \"codexContainmentCheckedVersion\": \"codex-cli 1.2.3+build\",\n" +
            "  \"asrLabFolder\": \"C:\\\\Tools\\\\asr\",\n" +
            "  \"asrLabPort\": 50124,\n" +
            "  \"hideFromScreenCapture\": false,\n" +
            "  \"ownedMappings\": [\n" +
            "    {\n" +
            "      \"mode\": \"private\",\n" +
            "      \"httpsPort\": 12443,\n" +
            "      \"path\": \"/\",\n" +
            "      \"target\": \"http://127.0.0.1:50123\",\n" +
            "      \"createdAt\": \"2026-10-10T08:15:30Z\"\n" +
            "    },\n" +
            "    {\n" +
            "      \"mode\": \"public\",\n" +
            "      \"httpsPort\": 8443,\n" +
            "      \"path\": \"/cue-2\",\n" +
            "      \"target\": \"http://127.0.0.1:50123\",\n" +
            "      \"createdAt\": \"2026-10-10T08:15:31Z\"\n" +
            "    }\n" +
            "  ]\n" +
            "}\n",
            Text(_files.ReadAllBytes(FilePath)));

        // No byte-order mark, and what was saved is what a load returns, the ledger included.
        Assert.Equal((byte)'{', _files.ReadAllBytes(FilePath)[0]);
        var loaded = store.Load();
        Assert.Equal(SettingsFileState.Loaded, loaded.State);
        Assert.True(loaded.Settings.SameValues(settings with
        {
            OwnedMappings =
            [
                settings.OwnedMappings[0],
                settings.OwnedMappings[1] with { CreatedAt = new DateTimeOffset(2026, 10, 10, 8, 15, 31, TimeSpan.Zero) },
            ],
        }));

        // Saving what was loaded writes the same bytes again.
        var first = _files.ReadAllBytes(FilePath);
        store.Save(loaded.Settings);
        Assert.Equal(first, _files.ReadAllBytes(FilePath));
    }

    [Fact]
    public void An_invalid_file_followed_by_a_save_keeps_valid_ledger_entries_and_leaves_the_invalid_copy()
    {
        var kept = Entry(ConnectionMode.Public, 10000, "/livecue");
        var failed = Put(FileText(
            privateHttpsPort: "443",
            ownedMappings: Array(EntryText("public", 10000, "/livecue"), EntryText("private", 70000, "/"))));
        _files.AddFile(InvalidCopyPath, "an older copy");
        var store = Store();

        // The application runs on what the load returned and saves that, as the desktop does.
        var loaded = store.Load();
        Assert.Equal(SettingsFileState.Invalid, loaded.State);
        Assert.Equal([kept], loaded.Settings.OwnedMappings);
        store.Save(loaded.Settings with { RelayPort = 50123 });

        // The failed file is beside the new one, byte for byte, in place of the older copy.
        Assert.Equal(failed, _files.ReadAllBytes(InvalidCopyPath));
        Assert.Equal([FilePath, InvalidCopyPath], DataFiles());

        // The new file is valid and still knows the mapping LiveCue created.
        var after = store.Load();
        Assert.Equal(SettingsFileState.Loaded, after.State);
        Assert.Equal(50123, after.Settings.RelayPort);
        Assert.Equal(11443, after.Settings.PrivateHttpsPort);
        Assert.Equal([kept], after.Settings.OwnedMappings);

        // A later save of a valid file makes no further copy.
        _files.AddFile(InvalidCopyPath, "marker");
        store.Save(after.Settings);
        Assert.Equal("marker", Text(_files.ReadAllBytes(InvalidCopyPath)));
    }

    [Fact]
    public void An_unreadable_file_is_copied_before_the_first_save_replaces_it()
    {
        var failed = Put("not json at all");
        var store = Store();

        store.Save(RelaySettings.Default);

        Assert.Equal(failed, _files.ReadAllBytes(InvalidCopyPath));
        Assert.Equal(DefaultText, Text(_files.ReadAllBytes(FilePath)));
    }

    [Fact]
    public void When_the_copy_of_a_failed_file_cannot_be_written_nothing_changes()
    {
        var failed = Put("not json at all");
        _files.WritesFail = true;

        var exception = Assert.Throws<IOException>(() => Store().Save(RelaySettings.Default));

        Assert.Equal("The settings file that failed could not be copied, so nothing was saved.", exception.Message);
        Assert.Equal(failed, _files.ReadAllBytes(FilePath));
        Assert.Equal([FilePath], DataFiles());
    }

    [Fact]
    public void With_the_real_file_system_a_save_replaces_the_file_and_leaves_no_pending_file()
    {
        using var data = new TempFolder();
        var files = new HostFileSystem();
        var folder = data.Combine("not-there-yet");
        var store = new SettingsStore(folder, files, new SettingsValidator(files));

        Assert.Equal(SettingsFileState.Missing, store.Load().State);
        Assert.False(Directory.Exists(folder));

        store.Save(new RelaySettings { RelayPort = 50001 });
        store.Save(new RelaySettings { RelayPort = 50002 });

        Assert.Equal(["settings.json"], FileNames(folder));
        Assert.Equal(50002, store.Load().Settings.RelayPort);
        Assert.StartsWith("{\n  \"schemaVersion\": 1,\n  \"relayPort\": 50002,\n", File.ReadAllText(store.FilePath), StringComparison.Ordinal);

        // A file someone broke by hand is kept as the invalid copy when the next save replaces it.
        File.WriteAllText(store.FilePath, "{ \"relayPort\": ");
        Assert.Equal(SettingsFileState.Unreadable, store.Load().State);
        store.Save(RelaySettings.Default);
        Assert.Equal("{ \"relayPort\": ", File.ReadAllText(store.InvalidCopyPath));
        Assert.Equal(DefaultText, File.ReadAllText(store.FilePath));
        Assert.Equal(["settings.json", "settings.json.invalid"], FileNames(folder));
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>The names of the files of a real folder, sorted.</summary>
    private static List<string> FileNames(string folder) =>
        [.. Directory.EnumerateFiles(folder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    private static OwnedMapping Entry(ConnectionMode mode, int httpsPort, string path, string target = "http://127.0.0.1:47841") =>
        new(mode, httpsPort, path, target, Created);

    private static string EntryText(string mode, int httpsPort, string path, string createdAt = "2026-10-10T08:15:30Z") =>
        $$"""{"mode":"{{mode}}","httpsPort":{{httpsPort}},"path":"{{path}}","target":"http://127.0.0.1:47841","createdAt":"{{createdAt}}"}""";

    private static string Array(params string[] entries) => "[" + string.Join(",", entries) + "]";

    private static string MemberLine(string member) => member switch
    {
        "schemaVersion" => "\"schemaVersion\": 1",
        "relayPort" => "\"relayPort\": 47841",
        "connectionMode" => "\"connectionMode\": \"private\"",
        "privateHttpsPort" => "\"privateHttpsPort\": 11443",
        "publicHttpsPort" => "\"publicHttpsPort\": 10000",
        "publicPathPrefix" => "\"publicPathPrefix\": \"/livecue\"",
        "codexExecutablePath" => "\"codexExecutablePath\": null",
        "codexContainmentCheckedVersion" => "\"codexContainmentCheckedVersion\": null",
        "asrLabFolder" => "\"asrLabFolder\": null",
        "asrLabPort" => "\"asrLabPort\": 47842",
        _ => throw new ArgumentOutOfRangeException(nameof(member)),
    };

    /// <summary>The member name of a field as the file writes it.</summary>
    private static string FieldName(SettingsField field)
    {
        var name = field.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>The default file with some members replaced by the given JSON values.</summary>
    private static string FileText(
        string? relayPort = null,
        string? privateHttpsPort = null,
        string? codexExecutablePath = null,
        string? asrLabFolder = null,
        string? ownedMappings = null)
    {
        var text = DefaultText;
        void Replace(string member, string? json)
        {
            if (json is not null)
            {
                text = text.Replace(MemberLine(member), $"\"{member}\": {json}", StringComparison.Ordinal);
            }
        }

        Replace("relayPort", relayPort);
        Replace("privateHttpsPort", privateHttpsPort);
        Replace("codexExecutablePath", codexExecutablePath);
        Replace("asrLabFolder", asrLabFolder);
        return ownedMappings is null
            ? text
            : text.Replace("\"ownedMappings\": []", "\"ownedMappings\": " + ownedMappings, StringComparison.Ordinal);
    }

    private SettingsStore Store() => new(DataDirectory, _files, new SettingsValidator(_files));

    /// <summary>Puts a settings file there, as if written by hand, and returns its bytes.</summary>
    private byte[] Put(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        _files.AddFile(FilePath, bytes);
        return bytes;
    }

    private List<string> DataFiles() =>
        [.. _files.FilePaths.Where(path => path.StartsWith(DataDirectory + @"\", StringComparison.OrdinalIgnoreCase))];

    /// <summary>A file system in which a settings file exists and every read and write is refused.</summary>
    private sealed class ThrowingFiles : IHostFileSystem
    {
        public bool FileIsThere { get; init; } = true;

        public string BaseDirectory => @"C:\LiveCue-tests\app";

        public string ProgramFilesDirectory => @"C:\Program Files";

        public bool IsOnFixedLocalDrive(string path) => true;

        public string? GetEnvironmentVariable(string name) => null;

        public bool FileExists(string path) => FileIsThere;

        public bool DirectoryExists(string path) => true;

        public void CreateDirectory(string path) => throw new UnauthorizedAccessException("Refused.");

        public byte[] ReadAllBytes(string path) => throw new UnauthorizedAccessException("Refused.");

        public void WriteAllBytesAtomic(string path, byte[] contents) => throw new UnauthorizedAccessException("Refused.");

        public IReadOnlyList<FoundFile> FindFiles(string directory, string fileName, int maxDepth) => [];
    }
}
