namespace LiveCue.Hosting;

/// <summary>
/// The folder that holds settings.json, pairing.json and schemas\ (architecture 3.5). It is
/// <c>%LOCALAPPDATA%\LiveCueRemake\</c>; the previous LiveCue's <c>%LOCALAPPDATA%\LiveCue\</c> is
/// never read or written.
/// </summary>
public static class DataFolder
{
    public const string FolderName = "LiveCueRemake";

    /// <summary>The default location for the signed-in Windows user.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary>The folder to use: <paramref name="overridePath"/> when one was given, otherwise the default.</summary>
    public static string Resolve(string? overridePath = null) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(overridePath) ? DefaultPath : overridePath);
}
