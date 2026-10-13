namespace LiveCue.Hosting;

/// <summary>
/// The folder that holds settings.json, pairing.json, schemas\, lab\ and work\ (architecture 3.5). It is
/// <c>%LOCALAPPDATA%\LiveCueRemake\</c>; the previous LiveCue's <c>%LOCALAPPDATA%\LiveCue\</c> is
/// never read or written. The folder is created by check 1 of the prerequisites, not here: resolving a
/// path touches no disk.
/// </summary>
public static class DataFolder
{
    public const string FolderName = "LiveCueRemake";

    /// <summary>The folder name of the previous LiveCue under <c>%LOCALAPPDATA%</c>. Never used.</summary>
    public const string PreviousAppFolderName = "LiveCue";

    /// <summary>The default location for the signed-in Windows user.</summary>
    public static string DefaultPath => Path.Combine(LocalApplicationData, FolderName);

    private static string LocalApplicationData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>The folder to use: <paramref name="overridePath"/> when one was given, otherwise the default.</summary>
    public static string Resolve(string? overridePath = null) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(overridePath) ? DefaultPath : overridePath);

    /// <summary>
    /// True when <paramref name="path"/> is the previous LiveCue's data folder or lies inside it. A host
    /// that takes a data folder from its command line refuses such a path, so that nothing of the old
    /// application is ever read or overwritten.
    /// </summary>
    public static bool IsPreviousAppFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var previous = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(LocalApplicationData, PreviousAppFolderName)));
            var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return candidate.Equals(previous, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(previous + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }
}
