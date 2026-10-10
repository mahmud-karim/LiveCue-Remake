using System.Security.Cryptography;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// What <see cref="StoredPairingExport"/> forwards to: the payload text of the pairing stored in a data
/// folder, read without a relay (the console host's <c>--show-pairing</c>). The file is read and
/// verified as a relay start reads it, and is never written, so this is safe while a relay runs on the
/// same folder. Apart from a relay's own export, the returned string is the only text that ever holds
/// the token.
/// </summary>
internal static class StoredPairingPayload
{
    /// <param name="dataDirectory">The data folder.</param>
    /// <param name="advertisedEndpoint">Any text of the grammar of protocol 4.3; the payload carries its
    /// canonical form.</param>
    /// <param name="protector">Null means the production protector.</param>
    /// <param name="files">Null means the production file system.</param>
    /// <param name="ct">Checked once, before the file is read.</param>
    /// <returns>The payload text of protocol 4.2, or null when there is no pairing file.</returns>
    /// <exception cref="ArgumentException">The endpoint fails the grammar.</exception>
    /// <exception cref="IOException">The file exists and fails a load rule. The message is fixed.</exception>
    public static Task<string?> ReadAsync(
        string dataDirectory,
        string advertisedEndpoint,
        IPairingProtector? protector,
        IPairingFiles? files,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!AdvertisedEndpoint.TryCanonicalize(advertisedEndpoint, out var endpoint))
        {
            throw new ArgumentException("The advertised endpoint is not valid.", nameof(advertisedEndpoint));
        }

        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<string?>(ct);
        }

        try
        {
            var store = new PairingStore(
                dataDirectory,
                protector ?? new DpapiPairingProtector(),
                files ?? new PairingFiles(),
                TimeProvider.System);
            return Task.FromResult(Read(store, endpoint));
        }
        catch (IOException exception)
        {
            return Task.FromException<string?>(exception);
        }
    }

    private static string? Read(PairingStore store, string endpoint)
    {
        Span<byte> token = stackalloc byte[PairingToken.Length];
        Span<byte> hash = stackalloc byte[PairingToken.HashLength];
        byte[]? payload = null;
        try
        {
            switch (store.Load(token, hash))
            {
                case PairingLoadResult.Missing:
                    return null;

                case PairingLoadResult.Loaded:
                    payload = PairingPayload.Utf8(endpoint, token);
                    return PairingPayload.Text(payload);

                default:
                    throw new IOException(PairingStore.UnreadableMessage);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
