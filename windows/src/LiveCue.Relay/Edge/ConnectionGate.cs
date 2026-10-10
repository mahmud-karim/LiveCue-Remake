using Microsoft.AspNetCore.Connections;

namespace LiveCue.Relay.Edge;

/// <summary>
/// Step P-0, the connection middleware of W-design 4.3: counts every open connection, upgraded or not,
/// and aborts a new one when the count would pass the cap (C-30), before a byte of it is parsed.
///
/// Kestrel's own two limits count plain and upgraded connections separately, and would together allow
/// C-30 plus C-32. A speech socket is a connection like any other here: it keeps its place from the
/// accept to the close.
/// </summary>
internal sealed class ConnectionGate(int capacity)
{
    private int _open;

    /// <summary>Connections that are open now, the ones being refused not counted.</summary>
    public int Open => Volatile.Read(ref _open);

    /// <summary>The delegate to hand to <c>ListenOptions.Use</c>.</summary>
    public ConnectionDelegate Wrap(ConnectionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return connection => RunAsync(connection, next);
    }

    private async Task RunAsync(ConnectionContext connection, ConnectionDelegate next)
    {
        if (Interlocked.Increment(ref _open) > capacity)
        {
            Interlocked.Decrement(ref _open);

            // Nothing is read and nothing is written: the caller sees a transport failure.
            connection.Abort();
            return;
        }

        try
        {
            await next(connection).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }
}
