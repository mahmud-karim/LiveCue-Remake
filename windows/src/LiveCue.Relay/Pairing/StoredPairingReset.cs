using System.Security.Cryptography;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// What <see cref="PairingMaintenance"/> forwards to: replaces <c>pairing.json</c> of a data folder with
/// a new pairing, whatever the old file contained (architecture 3.4, the recovery from an unreadable
/// pairing file). No relay is involved, and none may run on that folder meanwhile.
///
/// The new token is not handed out: the next relay start loads the file, and "Show QR" decrypts it.
/// A data folder that does not exist holds no pairing, so there is nothing to replace: nothing is
/// written, and the next start creates the folder's first pairing.
/// </summary>
internal static class StoredPairingReset
{
    /// <param name="dataDirectory">The data folder.</param>
    /// <param name="protector">Null means the production protector.</param>
    /// <param name="files">Null means the production file system.</param>
    /// <param name="ct">Checked once, before anything is written.</param>
    /// <exception cref="IOException">The file could not be replaced; the old file is unchanged. The
    /// message is fixed.</exception>
    public static Task RunAsync(string dataDirectory, IPairingProtector? protector, IPairingFiles? files, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        try
        {
            if (Directory.Exists(dataDirectory))
            {
                Replace(new PairingStore(
                    dataDirectory,
                    protector ?? new DpapiPairingProtector(),
                    files ?? new PairingFiles(),
                    TimeProvider.System));
            }

            return Task.CompletedTask;
        }
        catch (IOException exception)
        {
            return Task.FromException(exception);
        }
    }

    private static void Replace(PairingStore store)
    {
        Span<byte> token = stackalloc byte[PairingToken.Length];
        Span<byte> hash = stackalloc byte[PairingToken.HashLength];
        try
        {
            store.Create(token, hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
        }
    }
}
