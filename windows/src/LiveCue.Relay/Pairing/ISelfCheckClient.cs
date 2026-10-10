namespace LiveCue.Relay.Pairing;

/// <summary>Self-check seam (protocol 4.5).</summary>
internal interface ISelfCheckClient
{
    /// <summary>
    /// One GET with the three headers of protocol 4.5, redirects refused, bounded by <paramref name="ct"/>.
    /// True only for status 200 whose body has ok true and protocol 2. Every failure is false; only
    /// cancellation of <paramref name="ct"/> by the caller throws.
    /// </summary>
    Task<bool> GetHealthAsync(Uri healthUrl, string token, string nonce, CancellationToken ct);
}
