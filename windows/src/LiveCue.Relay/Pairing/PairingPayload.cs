namespace LiveCue.Relay.Pairing;

/// <summary>
/// The pairing payload text of protocol 4.2: exactly <c>{"endpoint":"…","token":"…"}</c>, members in
/// this order, no white space. Both values consist of characters that need no JSON escaping (the
/// canonical endpoint of protocol 4.3 and a token of shape C-02), so no serialiser is involved.
/// </summary>
internal static class PairingPayload
{
    public static string Build(string canonicalEndpoint, string token) =>
        string.Concat("{\"endpoint\":\"", canonicalEndpoint, "\",\"token\":\"", token, "\"}");
}
