namespace LiveCue.Relay.Pairing;

/// <summary>
/// What <see cref="PairingMaintenance"/> forwards to: replaces <c>pairing.json</c> with a new pairing,
/// whatever the old file contained (architecture 3.4, the recovery from an unreadable pairing file).
///
/// First version (foundation): the stand-in pairing keeps nothing on disk, so there is nothing to
/// replace and the call only checks its argument. Work package WP-W02 writes the file.
/// </summary>
internal static class StoredPairingReset
{
    /// <param name="protector">Null means the production protector.</param>
    /// <param name="files">Null means the production file system.</param>
    public static Task RunAsync(string dataDirectory, IPairingProtector? protector, IPairingFiles? files, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _ = protector;
        _ = files;
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
