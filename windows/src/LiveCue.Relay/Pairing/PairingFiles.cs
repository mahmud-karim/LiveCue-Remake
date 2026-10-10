using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>The production file system of the pairing store (W-design 4.7).</summary>
internal sealed class PairingFiles : IPairingFiles
{
    private const string PendingSuffix = ".pending";

    public bool Exists(string path) => File.Exists(path);

    /// <summary>
    /// The whole file. A file larger than W-05 is not read at all: it cannot be a pairing file, and the
    /// store calls it unreadable. The file is opened so that it can be replaced meanwhile: the console
    /// host may read the stored pairing while a relay on the same folder resets it.
    /// </summary>
    public byte[] ReadAllBytes(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > ProtocolConstants.MaxStateFileBytes)
        {
            throw new IOException("The file is larger than a pairing file can be.");
        }

        var contents = new byte[(int)stream.Length];
        stream.ReadExactly(contents);
        return contents;
    }

    /// <summary>
    /// Atomic replace: the new content goes to <c>path + ".pending"</c>, opened for nobody else, is
    /// flushed to the disk and closed, and then takes the place of <paramref name="path"/> in one rename.
    /// A pending file left by an interrupted write is overwritten.
    /// </summary>
    public void WriteAtomic(string path, ReadOnlySpan<byte> contents)
    {
        var pending = path + PendingSuffix;
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }

        File.Move(pending, path, overwrite: true);
    }
}
