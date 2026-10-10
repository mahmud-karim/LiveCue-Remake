using System.Buffers.Text;
using System.Security.Cryptography;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// Token shape, generation, hashing and the constant-time comparison (protocol 4.1, W-design 4.7).
///
/// A token is 32 random bytes written as 43 unpadded base64url characters (C-02). Because every
/// character of that alphabet is ASCII, the token's UTF-8 bytes are its characters one for one. The
/// relay works on those bytes in buffers it clears; the few places that need the token as text say so.
/// </summary>
internal static class PairingToken
{
    /// <summary>C-02: characters, and bytes, of a token.</summary>
    public const int Length = ProtocolConstants.TokenLength;

    /// <summary>Bytes of a SHA-256 hash.</summary>
    public const int HashLength = SHA256.HashSizeInBytes;

    /// <summary>Characters of a hash in the pairing file: lower-case hex.</summary>
    public const int HashTextLength = HashLength * 2;

    private const int RandomBytes = 32;

    /// <summary>
    /// Writes a new token into <paramref name="destination"/> (exactly <see cref="Length"/> bytes): 32
    /// bytes from the system's cryptographic generator, as unpadded base64url.
    /// </summary>
    public static void Generate(Span<byte> destination)
    {
        if (destination.Length != Length)
        {
            throw new ArgumentException("A token buffer holds exactly 43 bytes.", nameof(destination));
        }

        Span<byte> random = stackalloc byte[RandomBytes];
        try
        {
            RandomNumberGenerator.Fill(random);
            Base64Url.EncodeToUtf8(random, destination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(random);
        }
    }

    /// <summary>C-02 as a whole-text check: exactly 43 characters of the base64url alphabet.</summary>
    public static bool HasValidShape(ReadOnlySpan<char> token)
    {
        if (token.Length != Length)
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

    /// <summary>C-02 for a token held as bytes.</summary>
    public static bool HasValidShape(ReadOnlySpan<byte> token)
    {
        if (token.Length != Length)
        {
            return false;
        }

        foreach (var value in token)
        {
            if (!char.IsAsciiLetterOrDigit((char)value) && value is not ((byte)'_' or (byte)'-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>SHA-256 of the token's bytes into <paramref name="destination"/> (<see cref="HashLength"/> bytes).</summary>
    public static void Hash(ReadOnlySpan<byte> token, Span<byte> destination) => SHA256.HashData(token, destination);

    /// <summary>
    /// Step E-3: true when <paramref name="token"/> has the shape C-02 and hashes to
    /// <paramref name="acceptedHash"/>. The characters are encoded into a stack buffer, hashed into a
    /// stack buffer and compared in constant time. Allocates nothing.
    /// </summary>
    public static bool Matches(ReadOnlySpan<char> token, ReadOnlySpan<byte> acceptedHash)
    {
        if (!HasValidShape(token) || acceptedHash.Length != HashLength)
        {
            return false;
        }

        Span<byte> hash = stackalloc byte[HashLength];
        HashCharacters(token, hash);
        return CryptographicOperations.FixedTimeEquals(hash, acceptedHash);
    }

    /// <summary>The form of a hash in the pairing file: 64 lower-case hex characters.</summary>
    public static string HashText(ReadOnlySpan<byte> hash) => Convert.ToHexStringLower(hash);

    /// <summary>
    /// Reads a hash from its file form. The shape is <c>[a-f0-9]{64}</c> matched against the whole text:
    /// upper-case hex, a trailing line break or any other extra character is refused.
    /// </summary>
    public static bool TryParseHashText(ReadOnlySpan<char> text, Span<byte> destination)
    {
        if (text.Length != HashTextLength || destination.Length != HashLength)
        {
            return false;
        }

        for (var index = 0; index < HashLength; index++)
        {
            var high = LowerHexValue(text[index * 2]);
            var low = LowerHexValue(text[(index * 2) + 1]);
            if (high < 0 || low < 0)
            {
                return false;
            }

            destination[index] = (byte)((high << 4) | low);
        }

        return true;
    }

    /// <summary>The token's characters as text. Only the payload export and the self-check request need it.</summary>
    public static string ToText(ReadOnlySpan<byte> token) =>
        string.Create(token.Length, token, static (characters, bytes) =>
        {
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index] = (char)bytes[index];
            }
        });

    private static void HashCharacters(ReadOnlySpan<char> token, Span<byte> destination)
    {
        // The shape was checked: every character is ASCII, so one character is one byte.
        Span<byte> bytes = stackalloc byte[Length];
        try
        {
            for (var index = 0; index < Length; index++)
            {
                bytes[index] = (byte)token[index];
            }

            SHA256.HashData(bytes, destination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static int LowerHexValue(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        _ => -1,
    };
}
