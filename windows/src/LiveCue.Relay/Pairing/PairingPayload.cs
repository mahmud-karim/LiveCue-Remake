using System.Text;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The pairing payload of protocol 4.2: exactly <c>{"endpoint":"…","token":"…"}</c>, members in this
/// order, no white space. Both values consist of characters that need no JSON escaping (the canonical
/// endpoint of protocol 4.3 and a token of shape C-02) and are ASCII, so no serialiser is involved and
/// the text and its bytes are exact.
/// </summary>
internal static class PairingPayload
{
    private static ReadOnlySpan<byte> Head => "{\"endpoint\":\""u8;

    private static ReadOnlySpan<byte> Middle => "\",\"token\":\""u8;

    private static ReadOnlySpan<byte> Tail => "\"}"u8;

    /// <summary>
    /// The payload's bytes, which are what the QR code carries. The result holds the token: the caller
    /// clears it.
    /// </summary>
    /// <param name="canonicalEndpoint">The advertised endpoint in canonical form.</param>
    /// <param name="token">The token's bytes, of shape C-02.</param>
    public static byte[] Utf8(string canonicalEndpoint, ReadOnlySpan<byte> token)
    {
        if (!AdvertisedEndpoint.IsCanonical(canonicalEndpoint))
        {
            throw new ArgumentException("The endpoint is not in canonical form.", nameof(canonicalEndpoint));
        }

        if (!PairingToken.HasValidShape(token))
        {
            throw new ArgumentException("The bytes do not have the shape of a token.", nameof(token));
        }

        var payload = new byte[Head.Length + canonicalEndpoint.Length + Middle.Length + token.Length + Tail.Length];
        var rest = payload.AsSpan();
        Head.CopyTo(rest);
        rest = rest[Head.Length..];
        rest = rest[Encoding.ASCII.GetBytes(canonicalEndpoint, rest)..];
        Middle.CopyTo(rest);
        rest = rest[Middle.Length..];
        token.CopyTo(rest);
        Tail.CopyTo(rest[token.Length..]);
        return payload;
    }

    /// <summary>The payload as text, from its bytes. Only an explicit export makes this string.</summary>
    public static string Text(ReadOnlySpan<byte> payload) => Encoding.ASCII.GetString(payload);
}
