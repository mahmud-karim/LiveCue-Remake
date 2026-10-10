namespace LiveCue.Hosting;

public enum SettingsField
{
    SchemaVersion,
    RelayPort,
    ConnectionMode,
    PrivateHttpsPort,
    PublicHttpsPort,
    PublicPathPrefix,
    CodexExecutablePath,
    AsrLabFolder,
    AsrLabPort,
    OwnedMappings,
}

public enum SettingsProblemCode
{
    /// <summary>schemaVersion is not 1.</summary>
    UnsupportedVersion,

    /// <summary>A number outside its range.</summary>
    OutOfRange,

    /// <summary>Port 47831, or 8765 for the lab port.</summary>
    Forbidden,

    /// <summary>Equal to the other port it must differ from.</summary>
    Duplicate,

    /// <summary>Not one of the allowed values (mode, public port).</summary>
    NotAllowed,

    /// <summary>Does not match its pattern (prefix, ledger entry).</summary>
    BadFormat,

    /// <summary>Not an absolute path, or the file or folder does not exist.</summary>
    NotFound,

    /// <summary>The file is not named codex.exe.</summary>
    WrongFileName,

    /// <summary>The folder lacks one of the two launcher scripts.</summary>
    MissingScripts,
}

public sealed record SettingsProblem(SettingsField Field, SettingsProblemCode Code);

/// <summary>The rules of architecture 3.5 (table in section 4.14 of the W-design). Pure apart from the
/// file-system probes for the two paths.</summary>
public interface ISettingsValidator
{
    /// <summary>Every rule. Used before a save. Empty when valid. Order follows <see cref="SettingsField"/>.</summary>
    IReadOnlyList<SettingsProblem> Validate(RelaySettings settings);

    /// <summary>The rules that need no file system (everything except NotFound and MissingScripts). Used
    /// when the file is loaded, so that a folder deleted later does not invalidate the whole file.</summary>
    IReadOnlyList<SettingsProblem> ValidateStructure(RelaySettings settings);
}
