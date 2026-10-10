namespace LiveCue.Desktop.Tests.Support;

/// <summary>A clipboard that only remembers what was put on it, and can be "unavailable".</summary>
internal sealed class FakeClipboard : IClipboard
{
    /// <summary>False: every copy fails, as when another program holds the clipboard.</summary>
    public bool Available { get; set; } = true;

    /// <summary>Every text that was copied, in order.</summary>
    public List<string> Texts { get; } = [];

    public string? Last => Texts.Count == 0 ? null : Texts[^1];

    public bool TrySetText(string text)
    {
        if (!Available)
        {
            return false;
        }

        Texts.Add(text);
        return true;
    }
}
