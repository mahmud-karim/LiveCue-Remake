using System.Net.WebSockets;

namespace LiveCue.Relay.Speech;

/// <summary>What the speech route hands over after the upgrade.</summary>
/// <param name="ProtocolHeader">Value of X-LiveCue-Protocol, or null when absent or repeated.</param>
/// <param name="ModelHeader">Value of X-LiveCue-Speech-Model, or null when absent or repeated.</param>
/// <param name="UpgradeTimestamp">Clock timestamp of the upgrade; C-51 and C-55 count from it.</param>
internal sealed record SpeechConnection(
    WebSocket Socket,
    string? ProtocolHeader,
    string? ModelHeader,
    long UpgradeTimestamp,
    CancellationToken PairingRevoked);

/// <summary>Protocol section 10 from admission to the last close.</summary>
internal interface ISpeechBridge
{
    /// <summary>
    /// Runs one accepted socket: admission S-1 to S-6, the stream, the close. Completes when the phone
    /// socket and the lab socket are both closed and the lease is released. Never throws.
    /// </summary>
    Task RunAsync(SpeechConnection connection);
}
