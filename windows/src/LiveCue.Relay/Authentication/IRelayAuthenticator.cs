namespace LiveCue.Relay.Authentication;

/// <summary>
/// Decides whether a bearer token presented by a client is one the relay accepts. Asked on every
/// request, from request threads, so implementations must be thread-safe, quick, and must compare
/// in constant time.
/// </summary>
public interface IRelayAuthenticator
{
    /// <summary>Returns true when <paramref name="presentedToken"/> belongs to a paired phone.</summary>
    bool IsAccepted(string presentedToken);
}
