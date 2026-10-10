using LiveCue.Relay.Core;
using LiveCue.Relay.LocalModel;

namespace LiveCue.Relay.Speech;

/// <summary>
/// Protocol section 10 from admission to the last close: one <see cref="SpeechStream"/> per accepted
/// socket. The bridge itself holds no state between streams. What they share is the one speech lease
/// (C-34), the PC model controller and the lab connector.
/// </summary>
internal sealed class SpeechBridge : ISpeechBridge
{
    private readonly RelayContext _context;
    private readonly ILocalModelController _localModel;
    private readonly SpeechLease _lease;
    private readonly ILabSocketConnector _connector;

    public SpeechBridge(RelayContext context, ILocalModelController localModel, SpeechLease lease)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(localModel);
        ArgumentNullException.ThrowIfNull(lease);
        _context = context;
        _localModel = localModel;
        _lease = lease;
        _connector = context.Dependencies.LabSocketConnector ?? new LabSocketConnector(context);
    }

    public async Task RunAsync(SpeechConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            using var stream = new SpeechStream(_context, _localModel, _connector, _lease, connection);
            await stream.RunAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The contract: a stream never throws into the route. The socket is dropped instead.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _context.Events.Diagnostic("speech", "A speech stream could not be run.", exception);
            connection.Socket.Abort();
        }
    }
}
