namespace LiveCue.Hosting;

public enum SettingsFileState
{
    /// <summary>No file: defaults apply.</summary>
    Missing,
    Loaded,

    /// <summary>The file parsed but failed validation. It is left untouched; defaults apply, except
    /// that every ledger entry of the file that is valid by itself is kept.</summary>
    Invalid,

    /// <summary>The file could not be read or is not the expected JSON. Left untouched; defaults apply.</summary>
    Unreadable,
}

/// <param name="Settings">What to run on: the file's values when Loaded; otherwise the defaults, and for
/// Invalid the defaults with OwnedMappings set to the file's entries that are valid by themselves.</param>
public sealed record SettingsLoadResult(
    RelaySettings Settings,
    SettingsFileState State,
    IReadOnlyList<SettingsProblem> Problems);

/// <summary>Thrown by Save for settings that fail validation.</summary>
public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(IReadOnlyList<SettingsProblem> problems)
        : base("The settings are not valid.")
    {
        Problems = problems;
    }

    public IReadOnlyList<SettingsProblem> Problems { get; }
}

/// <summary>Owns settings.json. Not thread-safe for concurrent Save calls; the supervisor and the desktop
/// session serialise them.</summary>
public interface ISettingsStore
{
    /// <summary>Never throws.</summary>
    SettingsLoadResult Load();

    /// <summary>
    /// Validates, then writes atomically (write settings.json.pending, then replace). Throws
    /// <see cref="SettingsValidationException"/> or <see cref="IOException"/>. A failed save leaves the
    /// previous file intact. When the file on disk is Invalid or Unreadable, its bytes are first copied to
    /// settings.json.invalid (replacing an older copy); if that copy fails, nothing is written.
    /// </summary>
    void Save(RelaySettings settings);
}
