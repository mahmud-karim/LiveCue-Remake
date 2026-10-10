using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace LiveCue.Relay.Authentication;

/// <summary>
/// Accepts exactly one token. Only its SHA-256 hash is kept, and presented tokens are hashed and
/// compared in constant time, so neither the token nor its length leaks through timing.
/// </summary>
public sealed class SharedTokenAuthenticator : IRelayAuthenticator
{
    private readonly byte[] _acceptedHash;

    public SharedTokenAuthenticator(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _acceptedHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>Creates a new random token: 32 bytes from the system generator, as 43 base64url characters.</summary>
    public static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public bool IsAccepted(string presentedToken)
    {
        if (string.IsNullOrEmpty(presentedToken))
        {
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(presentedToken.Length));
        try
        {
            var written = Encoding.UTF8.GetBytes(presentedToken, buffer);
            Span<byte> presentedHash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(buffer.AsSpan(0, written), presentedHash);
            return CryptographicOperations.FixedTimeEquals(presentedHash, _acceptedHash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
