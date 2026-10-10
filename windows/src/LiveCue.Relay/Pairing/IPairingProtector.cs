namespace LiveCue.Relay.Pairing;

/// <summary>DPAPI seam (architecture 3.6). Failures throw <see cref="PairingProtectionException"/>.</summary>
internal interface IPairingProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedBytes);
}

/// <summary>Fixed message. The cause is never attached as InnerException.</summary>
internal sealed class PairingProtectionException : Exception
{
    public PairingProtectionException()
        : base("Protected pairing storage is not available.")
    {
    }
}
