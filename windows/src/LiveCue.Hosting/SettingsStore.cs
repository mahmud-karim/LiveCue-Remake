using System.Text.Json;

namespace LiveCue.Hosting;

/// <summary>Where a set of loaded settings came from.</summary>
public enum SettingsSource
{
    /// <summary>There is no settings file; these are the defaults.</summary>
    Defaults,

    /// <summary>Read from the settings file and valid.</summary>
    File,

    /// <summary>
    /// The settings file exists and is not usable. It was left untouched; these are the defaults, and
    /// the host says so until the user saves settings.
    /// </summary>
    DefaultsBecauseFileIsInvalid,
}

/// <summary>The outcome of loading settings: always usable settings, plus what was wrong with the file if anything.</summary>
public sealed record SettingsLoadResult(HostSettings Settings, SettingsSource Source, IReadOnlyList<string> Problems);

/// <summary>Loads and saves <see cref="HostSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>Never throws for a missing or broken file; see <see cref="SettingsSource"/>.</summary>
    SettingsLoadResult Load();

    /// <summary>Saves valid settings.</summary>
    /// <exception cref="ArgumentException">The settings break a rule of <see cref="HostSettings.Validate"/>.</exception>
    void Save(HostSettings settings);
}

/// <summary>
/// settings.json in the data folder: UTF-8, two-space indented, written atomically (write
/// <c>settings.json.pending</c>, then replace). A file that fails validation is never modified by loading.
/// </summary>
public sealed class FileSettingsStore(string dataDirectory) : ISettingsStore
{
    public const string FileName = "settings.json";

    public string FilePath { get; } = Path.Combine(dataDirectory, FileName);

    public SettingsLoadResult Load()
    {
        HostSettings? settings;
        try
        {
            using var stream = File.OpenRead(FilePath);
            settings = JsonSerializer.Deserialize(stream, HostingJsonContext.Default.HostSettings);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SettingsLoadResult(new HostSettings(), SettingsSource.Defaults, []);
        }
        catch (JsonException)
        {
            // The parser's own message can quote file content; a fixed sentence is enough here.
            return Invalid("settings.json is not valid JSON for LiveCue settings.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("settings.json could not be read.");
        }

        if (settings is null)
        {
            return Invalid("settings.json is empty.");
        }

        var problems = settings.Validate();
        return problems.Count == 0
            ? new SettingsLoadResult(settings, SettingsSource.File, [])
            : new SettingsLoadResult(new HostSettings(), SettingsSource.DefaultsBecauseFileIsInvalid, problems);
    }

    public void Save(HostSettings settings)
    {
        SettingsRules.RequireValid(settings);
        Directory.CreateDirectory(dataDirectory);

        var pending = FilePath + ".pending";
        using (var stream = File.Create(pending))
        {
            JsonSerializer.Serialize(stream, settings, HostingJsonContext.Default.HostSettings);
        }

        File.Move(pending, FilePath, overwrite: true);
    }

    private static SettingsLoadResult Invalid(string problem) =>
        new(new HostSettings(), SettingsSource.DefaultsBecauseFileIsInvalid, [problem]);
}

/// <summary>
/// Settings kept in memory only. The desktop self-test runs on this, so it reads and writes nothing
/// in the data folder.
/// </summary>
public sealed class InMemorySettingsStore(HostSettings? initial = null) : ISettingsStore
{
    private HostSettings _settings = initial ?? new HostSettings();

    public SettingsLoadResult Load() => new(_settings, SettingsSource.Defaults, []);

    public void Save(HostSettings settings)
    {
        SettingsRules.RequireValid(settings);
        _settings = settings;
    }
}

internal static class SettingsRules
{
    public static void RequireValid(HostSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var problems = settings.Validate();
        if (problems.Count > 0)
        {
            throw new ArgumentException("Settings are not valid: " + string.Join(" ", problems), nameof(settings));
        }
    }
}
