namespace LiveCue.Relay.Pairing;

/// <summary>File-system seam of the pairing store. Paths are absolute.</summary>
internal interface IPairingFiles
{
    bool Exists(string path);

    /// <summary>Throws IOException or UnauthorizedAccessException like File.ReadAllBytes.</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>Writes path + ".pending", flushes it to disk, then replaces <paramref name="path"/> in one
    /// rename. Either the old or the new content exists afterwards, never a mixture.</summary>
    void WriteAtomic(string path, ReadOnlySpan<byte> contents);
}
