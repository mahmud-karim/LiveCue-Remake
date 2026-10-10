using LiveCue.Relay.Core;

namespace LiveCue.Relay.Speech;

/// <summary>
/// Opens the lab's <c>/relay</c> socket (protocol 11.3).
///
/// First version (foundation): there is never a lab, so every connection attempt fails. Work package
/// WP-W05 builds the connector.
/// </summary>
internal sealed class LabSocketConnector : ILabSocketConnector
{
    public LabSocketConnector(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public Task<ILabSocket> ConnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new LabSocketException();
    }
}
