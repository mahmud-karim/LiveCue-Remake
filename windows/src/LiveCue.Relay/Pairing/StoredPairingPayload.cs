namespace LiveCue.Relay.Pairing;

/// <summary>
/// What <see cref="StoredPairingExport"/> forwards to: the payload text of the pairing stored in a data
/// folder, read without a relay and without changing the file (the console host's <c>--show-pairing</c>).
///
/// First version (foundation): the stand-in pairing keeps nothing on disk, so there is never a stored
/// pairing and the result is null. Work package WP-W02 reads and decrypts the file.
/// </summary>
internal static class StoredPairingPayload
{
    /// <param name="protector">Null means the production protector.</param>
    /// <param name="files">Null means the production file system.</param>
    public static Task<string?> ReadAsync(
        string dataDirectory,
        string advertisedEndpoint,
        IPairingProtector? protector,
        IPairingFiles? files,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!AdvertisedEndpoint.TryCanonicalize(advertisedEndpoint, out _))
        {
            throw new ArgumentException("The advertised endpoint is not valid.", nameof(advertisedEndpoint));
        }

        _ = protector;
        _ = files;
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
