using System.Globalization;
using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>
/// The rules of architecture 3.5 (table in W-design 4.14). Problems are reported in the order of
/// <see cref="SettingsField"/>; a field with several broken rules reports the first one of its row.
/// Every pattern is checked with a character loop over the whole text, so a value that ends in a line
/// break never passes (protocol section 1, shapes).
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

    /// <summary>The ports on which Tailscale Funnel can be switched on. A tailnet-only listener stays off them.</summary>
    internal static bool IsFunnelPort(int port) => port is 443 or 8443 or 10000;

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
            : IsFunnelPort(settings.PrivateHttpsPort) ? SettingsProblemCode.NotAllowed
            : settings.PrivateHttpsPort == settings.PublicHttpsPort ? SettingsProblemCode.Duplicate
            : null);

        Add(SettingsField.PublicHttpsPort,
            IsFunnelPort(settings.PublicHttpsPort) ? null : SettingsProblemCode.NotAllowed);

        Add(SettingsField.PublicPathPrefix,
            IsPathPrefix(settings.PublicPathPrefix) ? null : SettingsProblemCode.BadFormat);

        Add(SettingsField.CodexExecutablePath, CheckCodexPath(settings.CodexExecutablePath, askFileSystem));

        Add(SettingsField.CodexContainmentCheckedVersion,
            settings.CodexContainmentCheckedVersion is null || CodexSignInCheck.IsVersionText(settings.CodexContainmentCheckedVersion)
                ? null
                : SettingsProblemCode.BadFormat);

        Add(SettingsField.AsrLabFolder, CheckLabFolder(settings.AsrLabFolder, askFileSystem));

        Add(SettingsField.AsrLabPort,
            settings.AsrLabPort is < 1024 or > 65535 ? SettingsProblemCode.OutOfRange
            : settings.AsrLabPort is RelaySettings.ForbiddenPort or RelaySettings.LabContainerPort ? SettingsProblemCode.Forbidden
            : null);

        // hideFromScreenCapture has no rule: any boolean is valid.

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

        // The path form needs no file system: a UNC path, a device-namespace path, a relative path and
        // a drive-relative path are refused when the file is loaded, too.
        if (!HostFileSystem.IsDriveLetterPath(folder))
        {
            return SettingsProblemCode.NotAllowed;
        }

        if (!askFileSystem)
        {
            return null;
        }

        if (!_files.IsOnFixedLocalDrive(folder))
        {
            return SettingsProblemCode.NotAllowed;
        }

        if (!_files.DirectoryExists(folder))
        {
            return SettingsProblemCode.NotFound;
        }

        return LabFolderResolver.HoldsBothScripts(folder, _files) ? null : SettingsProblemCode.MissingScripts;
    }

    private static bool IsWellFormed(OwnedMapping mapping)
    {
        if (mapping is null || mapping.HttpsPort is < 1 or > 65535 || !IsRelayTarget(mapping.Target))
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

    /// <summary>Exactly "http://127.0.0.1:" and a port from 1024 to 65535 in its plain decimal form.</summary>
    private static bool IsRelayTarget(string? target)
    {
        if (target is null || !target.StartsWith(TargetPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = target.AsSpan(TargetPrefix.Length);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= 1024 and <= 65535
            && digits.SequenceEqual(port.ToString(CultureInfo.InvariantCulture));
    }
}
