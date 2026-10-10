namespace LiveCue.Hosting.Tests.Support;

/// <summary>
/// Settings kept in memory, with what a load reports scripted by the test. With a validator it refuses
/// invalid settings as the real store does; without one it accepts anything, so a test can hand the
/// supervisor settings no file could hold.
/// </summary>
internal sealed class InMemorySettingsStore(RelaySettings? initial = null, ISettingsValidator? validator = null) : ISettingsStore
{
    private RelaySettings _settings = initial ?? RelaySettings.Default;

    /// <summary>What a load reports about the file. Defaults apply unless it is Loaded or Missing.</summary>
    public SettingsFileState State { get; set; } = SettingsFileState.Missing;

    /// <summary>The problems a load reports with <see cref="SettingsFileState.Invalid"/>.</summary>
    public IReadOnlyList<SettingsProblem> Problems { get; set; } = [];

    /// <summary>True: every save throws IOException and keeps the old settings.</summary>
    public bool SaveFails { get; set; }

    public int Loads { get; private set; }

    public int Saves { get; private set; }

    /// <summary>What is stored, whatever a load would report.</summary>
    public RelaySettings Stored => _settings;

    public SettingsLoadResult Load()
    {
        Loads++;
        return State is SettingsFileState.Invalid or SettingsFileState.Unreadable
            ? new SettingsLoadResult(RelaySettings.Default, State, Problems)
            : new SettingsLoadResult(_settings, State, []);
    }

    public void Save(RelaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (validator?.Validate(settings) is { Count: > 0 } problems)
        {
            throw new SettingsValidationException(problems);
        }

        if (SaveFails)
        {
            throw new IOException("The settings could not be saved.");
        }

        Saves++;
        _settings = settings;
        State = SettingsFileState.Loaded;
    }
}
