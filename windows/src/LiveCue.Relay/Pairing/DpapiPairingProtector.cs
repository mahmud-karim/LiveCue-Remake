using System.Security.Cryptography;
using System.Text;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The production protector (architecture 3.6): DPAPI in-process, scope "current user", with the fixed
/// optional entropy. The pairing file therefore works only for the Windows account that created it.
/// The foundation's stand-in pairing does not use it yet; work package WP-W02 does.
/// </summary>
internal sealed class DpapiPairingProtector : IPairingProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LiveCueRemake pairing v1");

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        try
        {
            return ProtectedData.Protect(plaintext.ToArray(), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // The cause is never attached: its text is not shown or logged.
            throw new PairingProtectionException();
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes)
    {
        try
        {
            return ProtectedData.Unprotect(protectedBytes.ToArray(), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            throw new PairingProtectionException();
        }
    }
}
