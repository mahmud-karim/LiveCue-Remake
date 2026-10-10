namespace LiveCue.Hosting;

/// <summary>
/// Owns <c>settings.json</c> (architecture 3.5).
///
/// First version (foundation): the settings are kept in memory only. Nothing is read from or written to
/// the data folder, so every run starts from the defaults and a save lasts until the application closes.
/// Saving validates exactly as the real store will. Work package WP-W06 adds the file: its format, its
/// load rules and the atomic save.
/// </summary>
internal sealed class SettingsStore : ISettingsStore
{
    public const string FileName = "settings.json";

    private readonly ISettingsValidator _validator;
    private readonly Lock _gate = new();
    private RelaySettings _settings = RelaySettings.Default;
    private bool _saved;

    public SettingsStore(string dataDirectory, IHostFileSystem files, ISettingsValidator validator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(validator);
        FilePath = Path.Combine(dataDirectory, FileName);
        _validator = validator;
    }

    /// <summary>Where the file will live.</summary>
    public string FilePath { get; }

    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            return new SettingsLoadResult(_settings, _saved ? SettingsFileState.Loaded : SettingsFileState.Missing, []);
        }
    }

    public void Save(RelaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var problems = _validator.Validate(settings);
        if (problems.Count > 0)
        {
            throw new SettingsValidationException(problems);
        }

        lock (_gate)
        {
            _settings = settings;
            _saved = true;
        }
    }
}
