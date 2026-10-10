using LiveCue.Relay.Pairing;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// Reversible protection that is not DPAPI, so pairing tests run anywhere: a fixed header, then every
/// byte flipped. The result never contains the plaintext. It can be made to fail.
/// </summary>
internal sealed class FakePairingProtector : IPairingProtector
{
    private static readonly byte[] Header = "FAKE-PROTECTED:"u8.ToArray();

    /// <summary>True: protect and unprotect both fail, as DPAPI does for another Windows account.</summary>
    public bool Fails { get; set; }

    public int ProtectCalls { get; private set; }

    public int UnprotectCalls { get; private set; }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        ProtectCalls++;
        if (Fails)
        {
            throw new PairingProtectionException();
        }

        var result = new byte[Header.Length + plaintext.Length];
        Header.CopyTo(result, 0);
        for (var index = 0; index < plaintext.Length; index++)
        {
            result[Header.Length + index] = (byte)~plaintext[index];
        }

        return result;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes)
    {
        UnprotectCalls++;
        if (Fails || !protectedBytes.StartsWith(Header))
        {
            throw new PairingProtectionException();
        }

        var body = protectedBytes[Header.Length..];
        var result = new byte[body.Length];
        for (var index = 0; index < body.Length; index++)
        {
            result[index] = (byte)~body[index];
        }

        return result;
    }
}
