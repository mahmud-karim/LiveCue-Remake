using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>Token shape, generation, hashing and the constant-time comparison (protocol 4.1).</summary>
internal static class PairingToken
{
    /// <summary>Bytes of a SHA-256 hash.</summary>
    public const int HashLength = SHA256.HashSizeInBytes;

    /// <summary>32 bytes from the system's cryptographic generator, as 43 unpadded base64url characters.</summary>
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>C-02: exactly 43 characters of the base64url alphabet.</summary>
    public static bool HasValidShape(ReadOnlySpan<char> token)
    {
        if (token.Length != ProtocolConstants.TokenLength)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>SHA-256 of the UTF-8 token. This, never the token, is what the relay keeps.</summary>
    public static byte[] Hash(ReadOnlySpan<char> token)
    {
        var hash = new byte[HashLength];
        Hash(token, hash);
        return hash;
    }

    /// <summary>
    /// Step E-3: true when <paramref name="token"/> has the shape C-02 and hashes to
    /// <paramref name="acceptedHash"/>. Compared in constant time; allocates nothing.
    /// </summary>
    public static bool Matches(ReadOnlySpan<char> token, ReadOnlySpan<byte> acceptedHash)
    {
        if (!HasValidShape(token) || acceptedHash.Length != HashLength)
        {
            return false;
        }

        Span<byte> hash = stackalloc byte[HashLength];
        Hash(token, hash);
        return CryptographicOperations.FixedTimeEquals(hash, acceptedHash);
    }

    private static void Hash(ReadOnlySpan<char> token, Span<byte> destination)
    {
        Span<byte> utf8 = token.Length <= ProtocolConstants.TokenLength
            ? stackalloc byte[ProtocolConstants.TokenLength * 3]
            : new byte[Encoding.UTF8.GetMaxByteCount(token.Length)];
        var written = Encoding.UTF8.GetBytes(token, utf8);
        SHA256.HashData(utf8[..written], destination);
        CryptographicOperations.ZeroMemory(utf8);
    }
}
