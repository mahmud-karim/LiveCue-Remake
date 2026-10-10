using System.Security.Cryptography;
using System.Text;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The production protector (architecture 3.6): DPAPI in-process, scope "current user", with the fixed
/// optional entropy. The pairing file therefore works only for the Windows account that created it.
/// </summary>
internal sealed class DpapiPairingProtector : IPairingProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LiveCueRemake pairing v1");

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        // DPAPI takes an array. The copy is cleared as soon as it has been protected.
        var copy = plaintext.ToArray();
        try
        {
            return ProtectedData.Protect(copy, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException)
        {
            // The cause is never attached: its text is not shown or logged.
            throw new PairingProtectionException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    /// <summary>The plaintext. The caller clears it.</summary>
    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes)
    {
        try
        {
            return ProtectedData.Unprotect(protectedBytes.ToArray(), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException)
        {
            throw new PairingProtectionException();
        }
    }
}
