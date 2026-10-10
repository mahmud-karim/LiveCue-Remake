using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// Tokens of shape C-02 for tests. None is a real pairing token: each is one character repeated, the
/// synthetic form the protocol's own examples use.
/// </summary>
internal static class TestTokens
{
    /// <summary>Well formed and never accepted by any relay: 43 times <c>x</c> (protocol P2-002).</summary>
    public static readonly string WellFormedUnknown = new('x', ProtocolConstants.TokenLength);

    /// <summary>The token of the protocol's payload example: 43 times <c>A</c>.</summary>
    public static readonly string Example = new('A', ProtocolConstants.TokenLength);

    /// <summary>A token of the right alphabet and the wrong length.</summary>
    public static string OfLength(int length) => new('t', length);

    /// <summary>The value of an Authorization header for that token.</summary>
    public static string Bearer(string token) => "Bearer " + token;
}
