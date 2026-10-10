using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>Decides whether a presented token is the accepted one (protocol E-3).</summary>
internal interface ITokenVerifier
{
    /// <summary>
    /// True when <paramref name="token"/> hashes to the accepted hash. Called on request threads for
    /// every request, so it must be thread-safe and compare in constant time. The accepted hash is read
    /// at the moment of the call, so a reset takes effect on the next request.
    /// </summary>
    bool IsAccepted(ReadOnlySpan<char> token);
}

/// <summary>Token shape, generation and hashing (protocol 4.1).</summary>
internal static class PairingToken
{
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
        Span<byte> utf8 = token.Length <= ProtocolConstants.TokenLength
            ? stackalloc byte[ProtocolConstants.TokenLength * 3]
            : new byte[Encoding.UTF8.GetMaxByteCount(token.Length)];
        var written = Encoding.UTF8.GetBytes(token, utf8);
        var hash = SHA256.HashData(utf8[..written]);
        CryptographicOperations.ZeroMemory(utf8);
        return hash;
    }
}

/// <summary>Accepts the one token whose hash it was given, comparing in constant time.</summary>
internal sealed class HashedTokenVerifier(byte[] acceptedHash) : ITokenVerifier
{
    public static HashedTokenVerifier For(string token) => new(PairingToken.Hash(token));

    public bool IsAccepted(ReadOnlySpan<char> token) =>
        PairingToken.HasValidShape(token)
        && CryptographicOperations.FixedTimeEquals(PairingToken.Hash(token), acceptedHash);
}

/// <summary>
/// Stand-in for the pairing store until pairing is built: a token made at start, kept as a hash in
/// memory and shown to nobody. The relay therefore authenticates correctly, and no phone can pair yet.
/// </summary>
internal static class EphemeralPairing
{
    public static ITokenVerifier Create() => HashedTokenVerifier.For(PairingToken.Create());
}
