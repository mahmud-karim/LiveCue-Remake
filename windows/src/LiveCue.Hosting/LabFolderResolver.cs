namespace LiveCue.Hosting;

/// <summary>
/// Which lab folder a relay is given (W-design 4.14): <c>asrLabFolder</c> when it is set and still holds
/// both launcher scripts; when it is null, <c>asr\</c> beside the executable if that holds both scripts;
/// otherwise null, which means PC speech is "not configured".
///
/// The rule of the settings is applied again each time a folder is resolved, which is every relay start:
/// the folder that would be returned must be a drive-letter path on a fixed local volume. One that is
/// not gives null. So a <c>settings.json</c> edited by hand to a network share is "not configured", not
/// a path that reaches a process. The two script files only mark a folder as a lab folder; the relay
/// runs its own copies (architecture 3.8).
/// </summary>
internal static class LabFolderResolver
{
    public const string BundledFolderName = "asr";

    public static string? Resolve(RelaySettings settings, IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(files);
        return Resolve(settings.AsrLabFolder, files);
    }

    /// <summary>The same, for a folder that did not come from the settings (the console host's <c>--lab</c>).</summary>
    public static string? Resolve(string? configuredFolder, IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var folder = configuredFolder ?? Path.Combine(files.BaseDirectory, BundledFolderName);
        return IsOnFixedLocalDrive(folder, files) && HoldsBothScripts(folder, files) ? folder : null;
    }

    public static bool HoldsBothScripts(string folder, IHostFileSystem files) =>
        files.FileExists(Path.Combine(folder, SettingsValidator.StartScript))
        && files.FileExists(Path.Combine(folder, SettingsValidator.StopScript));

    private static bool IsOnFixedLocalDrive(string folder, IHostFileSystem files) =>
        HostFileSystem.IsDriveLetterPath(folder) && files.IsOnFixedLocalDrive(folder);
}
