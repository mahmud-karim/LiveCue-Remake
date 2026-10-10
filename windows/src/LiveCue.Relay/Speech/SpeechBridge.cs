using System.Net.WebSockets;
using LiveCue.Relay.Core;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>
/// Protocol section 10 from admission to the last close.
///
/// First version (foundation): every accepted socket is refused the same way. It gets one
/// <c>error</c> frame with LAB_NOT_CONFIGURED and is closed with 1008 and that code as the reason; the
/// bridge then waits C-58 for the phone's close and aborts. Nothing is prepared and no lab is contacted.
/// Work package WP-W05 builds the bridge.
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
        _ = _localModel;
        _ = _lease;
        _ = _connector;

        var socket = connection.Socket;
        var error = RelayErrors.LabNotConfigured;
        try
        {
            using var limit = new CancellationTokenSource(ProtocolConstants.CloseHandshakeWait, _context.Clock);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, _context.Access.Stopping);

            await socket.SendAsync(SpeechFrames.Error(error), WebSocketMessageType.Text, endOfMessage: true, wait.Token)
                .ConfigureAwait(false);
            await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, error.Code, wait.Token).ConfigureAwait(false);

            // Wait for the phone's close frame; whatever it still sends is not read as a command.
            var scratch = new byte[256];
            while (socket.State is WebSocketState.CloseSent or WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(scratch.AsMemory(), wait.Token).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
        {
            // The phone went away, or did not close in time.
        }
        finally
        {
            if (socket.State is not (WebSocketState.Closed or WebSocketState.Aborted))
            {
                socket.Abort();
            }
        }
    }
}
