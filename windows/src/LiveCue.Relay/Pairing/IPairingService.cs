namespace LiveCue.Relay.Pairing;

/// <summary>The pairing area as the edge, the host, the answers and the speech bridge use it.</summary>
internal interface IPairingService
{
    /// <summary>
    /// Start: loads pairing.json or, only when the file does not exist, creates a pairing. Returns true
    /// when one was created. Any other load problem throws RelayStartException(PairingStoreUnreadable) and
    /// leaves the file untouched.
    /// </summary>
    Task<bool> LoadOrCreateAsync(CancellationToken ct);

    /// <summary>Step E-3: SHA-256 of the 43 characters against the accepted hash, constant time. Reads the
    /// accepted hash at the moment of the call. Allocation-free.</summary>
    bool Verify(ReadOnlySpan<char> token);

    /// <summary>A token that is cancelled when the pairing accepted right now is revoked by a reset.
    /// The edge captures it immediately after Verify returned true.</summary>
    CancellationToken CurrentRevocation { get; }

    /// <summary>Protocol 5.5: true when <paramref name="headerValue"/> equals the nonce of the self-check
    /// in progress (constant time). False when no check is in progress.</summary>
    bool IsSelfCheckNonce(ReadOnlySpan<char> headerValue);

    /// <summary>The edge reports that the self-check request arrived, and whether it carried
    /// Tailscale-Funnel-Request.</summary>
    void NoteSelfCheckArrival(bool throughFunnel);

    Task<PairingQr> ShowAsync(CancellationToken ct);

    Task<PairingQr> ResetAsync(CancellationToken ct);

    Task<string> GetPayloadAsync(CancellationToken ct);
}
