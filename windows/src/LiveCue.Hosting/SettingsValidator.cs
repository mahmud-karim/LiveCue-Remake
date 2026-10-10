using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>
/// The rules of architecture 3.5 (table in W-design 4.14). Problems are reported in the order of
/// <see cref="SettingsField"/>; a field with several broken rules reports the first one.
/// </summary>
internal sealed class SettingsValidator : ISettingsValidator
{
    public const string CodexFileName = "codex.exe";
    public const string StartScript = "Start-ASR-Lab.ps1";
    public const string StopScript = "Stop-ASR-Lab.ps1";

    private const string TargetPrefix = "http://127.0.0.1:";

    private readonly IHostFileSystem _files;

    public SettingsValidator(IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
    }

    public IReadOnlyList<SettingsProblem> Validate(RelaySettings settings) => Run(settings, askFileSystem: true);

    public IReadOnlyList<SettingsProblem> ValidateStructure(RelaySettings settings) => Run(settings, askFileSystem: false);

    public bool IsValidLedgerEntry(OwnedMapping entry) => IsWellFormed(entry);

    /// <summary>The prefix rule of architecture 3.5: "/", then 1 to 32 of [a-z0-9-], not starting with "-".</summary>
    internal static bool IsPathPrefix(string? prefix)
    {
        if (prefix is null || prefix.Length is < 2 or > 33 || prefix[0] != '/' || prefix[1] == '-')
        {
            return false;
        }

        foreach (var character in prefix.AsSpan(1))
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private List<SettingsProblem> Run(RelaySettings settings, bool askFileSystem)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var problems = new List<SettingsProblem>();
        void Add(SettingsField field, SettingsProblemCode? code)
        {
            if (code is { } problem)
            {
                problems.Add(new SettingsProblem(field, problem));
            }
        }

        Add(SettingsField.SchemaVersion,
            settings.SchemaVersion == RelaySettings.CurrentSchemaVersion ? null : SettingsProblemCode.UnsupportedVersion);

        Add(SettingsField.RelayPort,
            settings.RelayPort is < 1024 or > 65535 ? SettingsProblemCode.OutOfRange
            : settings.RelayPort == RelaySettings.ForbiddenPort ? SettingsProblemCode.Forbidden
            : settings.RelayPort == settings.AsrLabPort ? SettingsProblemCode.Duplicate
            : null);

        Add(SettingsField.ConnectionMode,
            settings.ConnectionMode is ConnectionMode.Private or ConnectionMode.Public ? null : SettingsProblemCode.NotAllowed);

        Add(SettingsField.PrivateHttpsPort,
            settings.PrivateHttpsPort is < 1 or > 65535 ? SettingsProblemCode.OutOfRange
            : settings.PrivateHttpsPort == settings.PublicHttpsPort ? SettingsProblemCode.Duplicate
            : null);

        Add(SettingsField.PublicHttpsPort,
            settings.PublicHttpsPort is 443 or 8443 or 10000 ? null : SettingsProblemCode.NotAllowed);

        Add(SettingsField.PublicPathPrefix,
            IsPathPrefix(settings.PublicPathPrefix) ? null : SettingsProblemCode.BadFormat);

        Add(SettingsField.CodexExecutablePath, CheckCodexPath(settings.CodexExecutablePath, askFileSystem));
        Add(SettingsField.AsrLabFolder, CheckLabFolder(settings.AsrLabFolder, askFileSystem));

        Add(SettingsField.AsrLabPort,
            settings.AsrLabPort is < 1024 or > 65535 ? SettingsProblemCode.OutOfRange
            : settings.AsrLabPort is RelaySettings.ForbiddenPort or RelaySettings.LabContainerPort ? SettingsProblemCode.Forbidden
            : null);

        Add(SettingsField.OwnedMappings,
            settings.OwnedMappings is null || !settings.OwnedMappings.All(IsWellFormed) ? SettingsProblemCode.BadFormat : null);

        return problems;
    }

    private SettingsProblemCode? CheckCodexPath(string? path, bool askFileSystem)
    {
        if (path is null)
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return SettingsProblemCode.NotFound;
        }

        if (!string.Equals(Path.GetFileName(path), CodexFileName, StringComparison.OrdinalIgnoreCase))
        {
            return SettingsProblemCode.WrongFileName;
        }

        return askFileSystem && !_files.FileExists(path) ? SettingsProblemCode.NotFound : null;
    }

    private SettingsProblemCode? CheckLabFolder(string? folder, bool askFileSystem)
    {
        if (folder is null)
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(folder))
        {
            return SettingsProblemCode.NotFound;
        }

        if (!askFileSystem)
        {
            return null;
        }

        if (!_files.DirectoryExists(folder))
        {
            return SettingsProblemCode.NotFound;
        }

        return LabFolderResolver.HoldsBothScripts(folder, _files) ? null : SettingsProblemCode.MissingScripts;
    }

    private static bool IsWellFormed(OwnedMapping mapping)
    {
        if (mapping is null
            || mapping.HttpsPort is < 1 or > 65535
            || mapping.Target is null
            || !mapping.Target.StartsWith(TargetPrefix, StringComparison.Ordinal)
            || !int.TryParse(
                mapping.Target.AsSpan(TargetPrefix.Length),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var port)
            || port is < 1024 or > 65535)
        {
            return false;
        }

        return mapping.Mode switch
        {
            ConnectionMode.Private => mapping.Path == "/",
            ConnectionMode.Public => IsPathPrefix(mapping.Path),
            _ => false,
        };
    }
}
