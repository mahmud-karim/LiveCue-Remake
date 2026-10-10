using System.Threading.Channels;

namespace LiveCue.Relay;

/// <summary>
/// One relay instance: the in-process replacement of the old child process (architecture 3.4).
/// All members are thread-safe. Disposing a running relay stops it first.
/// </summary>
public interface IRelay : IAsyncDisposable
{
    /// <summary>Current life-cycle state. Changes before the matching event is published.</summary>
    RelayStatus Status { get; }

    /// <summary>False while paused. A new instance is always accepting.</summary>
    bool Accepting { get; }

    /// <summary>The bound loopback port while Running, otherwise 0.</summary>
    int BoundPort { get; }

    /// <summary>
    /// Every event, in order. One bounded channel (W-01), oldest dropped when full. Single reader.
    /// Completed after <see cref="RelayStopped"/> has been written.
    /// </summary>
    ChannelReader<RelayEvent> Events { get; }

    /// <summary>
    /// Validates the endpoint, loads or creates the pairing, writes the schema files, binds the listener.
    /// On success publishes <see cref="RelayStarted"/> (and <see cref="Notice"/> PairingRestored for an
    /// existing pairing) and returns with Status Running. On failure publishes <see cref="RelayFaulted"/>,
    /// leaves nothing listening, sets Status Faulted and throws <see cref="RelayStartException"/>.
    /// A second call throws <see cref="InvalidOperationException"/>.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Graceful, bounded by W-02: stops accepting, kills every answer run, ends every speech stream with
    /// RELAY_STOPPING, closes the listener, aborts what is still open. Publishes <see cref="RelayStopped"/>.
    /// Idempotent; never throws for a relay that is already stopped or faulted.
    /// </summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Pause (false) or resume (true). Takes effect before the task completes; then publishes
    /// <see cref="AcceptingChanged"/>. Calling it with the current value does nothing and publishes nothing.
    /// Throws <see cref="InvalidOperationException"/> unless Running.
    /// </summary>
    Task SetAcceptingAsync(bool accepting);

    /// <summary>
    /// Decrypts the saved token and returns the matrix of the same pairing payload. The file is not
    /// modified. Publishes <see cref="PairingShown"/>, then starts one self-check in the background whose
    /// result arrives as <see cref="SelfCheckCompleted"/>. Calls are serialised with ResetPairingAsync.
    /// Throws <see cref="InvalidOperationException"/> unless Running.
    /// </summary>
    Task<PairingQr> ShowPairingAsync(CancellationToken ct);

    /// <summary>
    /// Replaces the pairing (protocol 4.4, "Reset pairing"). The old token is rejected from the moment the
    /// task completes; in-flight answers of the old token end with 401 and open streams with
    /// PAIRING_CHANGED. Publishes <see cref="Notice"/> PairingReset, <see cref="PairingShown"/>, then
    /// self-checks as above. If the file cannot be replaced the old pairing stays valid and the call throws
    /// <see cref="IOException"/> with a fixed message.
    /// </summary>
    Task<PairingQr> ResetPairingAsync(CancellationToken ct);
}

/// <summary>What the desktop may hold of a pairing: the advertised endpoint and the module matrix.</summary>
/// <param name="Endpoint">Canonical advertised endpoint.</param>
/// <param name="Modules">Square, row-major, true = dark, no quiet zone. At least 21 rows.</param>
public sealed record PairingQr(string Endpoint, bool[][] Modules);

/// <summary>
/// The pairing payload as text (protocol 4.2). Implemented by the relay object and resolved only by the
/// console host for <c>--show-pairing</c>. <c>LiveCue.Desktop</c> never references this type; a test proves it.
/// </summary>
public interface IPairingExport
{
    /// <summary>Throws <see cref="InvalidOperationException"/> unless the relay is Running.</summary>
    Task<string> GetPairingPayloadAsync(CancellationToken ct);
}
