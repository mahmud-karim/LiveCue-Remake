namespace LiveCue.Relay.Pairing;

/// <summary>
/// The production file system of the pairing store (W-design 4.7, "Atomic write"). The foundation's
/// stand-in pairing does not use it yet; work package WP-W02 does.
/// </summary>
internal sealed class PairingFiles : IPairingFiles
{
    public bool Exists(string path) => File.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAtomic(string path, ReadOnlySpan<byte> contents)
    {
        var pending = path + ".pending";
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }

        File.Move(pending, path, overwrite: true);
    }
}
