namespace LiveCue.Relay.Pairing;

/// <summary>
/// The production self-check client (protocol 4.5): one GET of the advertised health URL with the three
/// headers, redirects refused, at most W-06 bytes read.
///
/// First version (foundation): makes no request and reports "not verified". Work package WP-W02 builds it.
/// </summary>
internal sealed class SelfCheckClient : ISelfCheckClient
{
    public Task<bool> GetHealthAsync(Uri healthUrl, string token, string nonce, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
