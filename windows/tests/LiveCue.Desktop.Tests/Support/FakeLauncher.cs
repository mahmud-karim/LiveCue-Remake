namespace LiveCue.Desktop.Tests.Support;

/// <summary>A launcher that opens nothing and remembers what it was asked to open.</summary>
internal sealed class FakeLauncher : IExternalLauncher
{
    /// <summary>False: every open fails.</summary>
    public bool Succeeds { get; set; } = true;

    public List<Uri> Urls { get; } = [];

    public List<string> Files { get; } = [];

    public bool OpenUrl(Uri url)
    {
        Urls.Add(url);
        return Succeeds;
    }

    public bool OpenFile(string path)
    {
        Files.Add(path);
        return Succeeds;
    }
}
