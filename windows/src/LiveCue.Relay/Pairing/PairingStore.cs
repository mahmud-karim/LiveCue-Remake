using System.Security.Cryptography;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>What <see cref="PairingStore.Load"/> found.</summary>
internal enum PairingLoadResult
{
    /// <summary>There is no pairing file. This, and only this, leads to creating a pairing.</summary>
    Missing,

    /// <summary>The file passed every load rule; the token and its hash were handed out.</summary>
    Loaded,

    /// <summary>The file exists and failed a load rule. It was not touched.</summary>
    Unreadable,
}

/// <summary>
/// <c>pairing.json</c> of one data folder: the load rules and the atomic replace (W-design 4.7).
///
/// The token never leaves this class except into a buffer the caller owns and clears. What is written
/// is its SHA-256 and a copy protected for the current Windows user, never the token itself.
/// </summary>
internal sealed class PairingStore
{
    /// <summary>The fixed text of every failed read: the text of the start fault.</summary>
    public static readonly string UnreadableMessage = FixedTexts.Fault(RelayFaultCode.PairingStoreUnreadable);

    /// <summary>The fixed text of every failed write.</summary>
    public const string WriteFailedMessage = "The pairing could not be saved.";

    private readonly string _path;
    private readonly IPairingProtector _protector;
    private readonly IPairingFiles _files;
    private readonly TimeProvider _clock;

    public PairingStore(string dataDirectory, IPairingProtector protector, IPairingFiles files, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(clock);
        _path = PairingFile.PathIn(dataDirectory);
        _protector = protector;
        _files = files;
        _clock = clock;
    }

    /// <summary>
    /// Reads and verifies the file. Every one of these must hold, or the result is
    /// <see cref="PairingLoadResult.Unreadable"/>: the file can be read and has at most W-05 bytes; it is
    /// a JSON object; <c>schemaVersion</c> is the number 1; <c>tokenHash</c> is 64 lower-case hex
    /// characters; <c>protectedToken</c> is Base64 that unprotects to exactly C-02 characters of the token
    /// alphabet; their SHA-256 equals <c>tokenHash</c> (compared in constant time). The file is never
    /// written here, and a leftover <c>pairing.json.pending</c> is not looked at.
    /// </summary>
    /// <param name="token">Receives the token's bytes on <see cref="PairingLoadResult.Loaded"/>:
    /// <see cref="PairingToken.Length"/> bytes that the caller clears.</param>
    /// <param name="hash">Receives the hash on <see cref="PairingLoadResult.Loaded"/>:
    /// <see cref="PairingToken.HashLength"/> bytes.</param>
    public PairingLoadResult Load(Span<byte> token, Span<byte> hash)
    {
        if (!_files.Exists(_path))
        {
            return PairingLoadResult.Missing;
        }

        byte[] contents;
        try
        {
            contents = _files.ReadAllBytes(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PairingLoadResult.Unreadable;
        }

        Span<byte> storedHash = stackalloc byte[PairingToken.HashLength];
        if (contents.Length > ProtocolConstants.MaxStateFileBytes
            || !PairingFile.TryRead(contents, storedHash, out var protectedToken))
        {
            return PairingLoadResult.Unreadable;
        }

        byte[] plaintext;
        try
        {
            plaintext = _protector.Unprotect(protectedToken);
        }
        catch (PairingProtectionException)
        {
            // Another Windows account, another PC, or a damaged copy. The cause is not kept.
            return PairingLoadResult.Unreadable;
        }

        try
        {
            if (!PairingToken.HasValidShape(plaintext))
            {
                return PairingLoadResult.Unreadable;
            }

            Span<byte> computed = stackalloc byte[PairingToken.HashLength];
            PairingToken.Hash(plaintext, computed);
            if (!CryptographicOperations.FixedTimeEquals(computed, storedHash))
            {
                return PairingLoadResult.Unreadable;
            }

            plaintext.CopyTo(token);
            storedHash.CopyTo(hash);
            return PairingLoadResult.Loaded;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Creates a pairing and replaces the file with it in one rename: a new token, its hash and its
    /// protected copy. When this throws, the file is what it was before and no pairing was created.
    /// </summary>
    /// <param name="token">Receives the new token's bytes: <see cref="PairingToken.Length"/> bytes that
    /// the caller clears.</param>
    /// <param name="hash">Receives its hash: <see cref="PairingToken.HashLength"/> bytes.</param>
    /// <exception cref="IOException">The copy could not be protected or the file could not be written.
    /// The message is <see cref="WriteFailedMessage"/>; the cause is not attached.</exception>
    public void Create(Span<byte> token, Span<byte> hash)
    {
        PairingToken.Generate(token);
        PairingToken.Hash(token, hash);

        byte[] protectedToken;
        try
        {
            protectedToken = _protector.Protect(token);
        }
        catch (PairingProtectionException)
        {
            throw new IOException(WriteFailedMessage);
        }

        var contents = PairingFile.Write(hash, protectedToken, _clock.GetUtcNow());
        try
        {
            _files.WriteAtomic(_path, contents);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(WriteFailedMessage);
        }
    }
}
