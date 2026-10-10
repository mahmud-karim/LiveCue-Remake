using System.Threading.Channels;

namespace LiveCue.Relay;

/// <summary>Where a relay instance is in its one life.</summary>
public enum RelayStatus
{
    /// <summary>Not listening: not started yet, or stopped.</summary>
    Stopped,

    /// <summary>Binding the port.</summary>
    Starting,

    /// <summary>Listening and answering requests.</summary>
    Running,

    /// <summary>Closing the listener.</summary>
    Stopping,

    /// <summary>The start failed, or a fatal fault ended the relay.</summary>
    Faulted,
}

/// <summary>
/// The in-process interface between a host and the relay (architecture 3.4). It replaces the old
/// child process with its stdin commands and stdout events, and carries the same information.
///
/// One instance is one run: create it with <see cref="RelayFactory"/>, start it once, stop it once.
/// A restarted relay is a new instance.
///
/// The spike has the life-cycle members. Pause and resume (<c>Accepting</c>, <c>SetAcceptingAsync</c>)
/// and pairing (<c>ShowPairingAsync</c>, <c>ResetPairingAsync</c>) are added to this interface when
/// those features are built.
/// </summary>
public interface IRelay : IAsyncDisposable
{
    /// <summary>Current life-cycle state.</summary>
    RelayStatus Status { get; }

    /// <summary>The loopback port in use while <see cref="Status"/> is Running; otherwise 0.</summary>
    int BoundPort { get; }

    /// <summary>
    /// Every event, in order. The channel is bounded and drops the oldest event when full, so raising
    /// an event never blocks a request. It completes when the relay is disposed. Events are written
    /// from thread-pool threads; a host that touches UI marshals to its own thread.
    /// </summary>
    ChannelReader<RelayEvent> Events { get; }

    /// <summary>Binds the port and starts answering requests.</summary>
    /// <exception cref="RelayStartException">The relay could not start; <see cref="RelayFaulted"/> was raised too.</exception>
    /// <exception cref="InvalidOperationException">This instance was already started.</exception>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops answering requests and releases the port. Graceful and bounded: connections still open
    /// after <see cref="Protocol.ProtocolConstants.StopTimeout"/> are aborted. Does nothing if the
    /// relay is not running.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>Creates relay instances.</summary>
public static class RelayFactory
{
    /// <summary>Creates a relay that has not been started.</summary>
    public static IRelay Create(RelayOptions options, RelayDependencies? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new RelayServer(options, dependencies ?? new RelayDependencies());
    }
}

/// <summary>The relay could not start. <see cref="RelayFaulted"/> with the same code was raised too.</summary>
public sealed class RelayStartException : Exception
{
    public RelayStartException()
        : this(RelayFaultCode.StartFailed, "Could not start the relay.", null)
    {
    }

    public RelayStartException(string message)
        : this(RelayFaultCode.StartFailed, message, null)
    {
    }

    public RelayStartException(string message, Exception? innerException)
        : this(RelayFaultCode.StartFailed, message, innerException)
    {
    }

    public RelayStartException(RelayFaultCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>The fault, for hosts that word their own message from it.</summary>
    public RelayFaultCode Code { get; }
}
