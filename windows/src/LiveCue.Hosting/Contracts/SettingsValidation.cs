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
    CodexContainmentCheckedVersion,
    AsrLabFolder,
    AsrLabPort,
    HideFromScreenCapture,
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

    /// <summary>Not one of the allowed values (mode, public port); a private port of 443, 8443 or 10000;
    /// a lab folder that is not a drive-letter path on a fixed local volume (UNC, device namespace,
    /// relative, drive-relative, network or removable drive).</summary>
    NotAllowed,

    /// <summary>Does not match its pattern as a whole text (prefix, ledger entry, Codex version text).</summary>
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

    /// <summary>The rules that need no file system (everything except NotFound, MissingScripts and the
    /// fixed-local-volume half of the lab folder rule; the path-form half is checked here). Used when the
    /// file is loaded, so that a folder deleted later does not invalidate the whole file.</summary>
    IReadOnlyList<SettingsProblem> ValidateStructure(RelaySettings settings);

    /// <summary>True when one ledger entry passes the ownedMappings rule by itself. Used to keep the
    /// valid entries of a file that failed validation as a whole (architecture 3.5).</summary>
    bool IsValidLedgerEntry(OwnedMapping entry);
}
