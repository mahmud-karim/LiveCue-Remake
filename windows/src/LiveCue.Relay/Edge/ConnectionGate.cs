namespace LiveCue.Relay.Edge;

/// <summary>
/// The connection middleware of W-design 4.3: counts every open connection, upgraded or not, and
/// aborts a new one when the count would pass C-30.
///
/// First version (foundation): nothing yet. Work package WP-W01 builds it.
/// </summary>
internal static class ConnectionGate
{
}
