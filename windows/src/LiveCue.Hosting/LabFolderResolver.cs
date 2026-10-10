namespace LiveCue.Hosting;

/// <summary>
/// Which lab folder a relay is given (W-design 4.14): <c>asrLabFolder</c> when it is set and still holds
/// both launcher scripts; when it is null, <c>asr\</c> beside the executable if that holds both scripts;
/// otherwise null, which means PC speech is "not configured".
/// </summary>
internal static class LabFolderResolver
{
    public const string BundledFolderName = "asr";

    public static string? Resolve(RelaySettings settings, IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(files);

        if (settings.AsrLabFolder is { } configured)
        {
            return HoldsBothScripts(configured, files) ? configured : null;
        }

        var bundled = Path.Combine(files.BaseDirectory, BundledFolderName);
        return HoldsBothScripts(bundled, files) ? bundled : null;
    }

    public static bool HoldsBothScripts(string folder, IHostFileSystem files) =>
        files.FileExists(Path.Combine(folder, SettingsValidator.StartScript))
        && files.FileExists(Path.Combine(folder, SettingsValidator.StopScript));
}
