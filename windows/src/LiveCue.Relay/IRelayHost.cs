namespace LiveCue.Relay;

/// <summary>Where a relay host is in its life cycle.</summary>
public enum RelayHostState
{
    /// <summary>Not listening.</summary>
    Stopped,

    /// <summary>Binding the port.</summary>
    Starting,

    /// <summary>Listening and answering requests.</summary>
    Running,

    /// <summary>Draining requests and releasing the port.</summary>
    Stopping,
}

/// <summary>
/// The in-process surface of the relay: start it, stop it and hear what happens. The desktop app
/// and the console host both drive the relay through this interface and nothing else.
/// </summary>
public interface IRelayHost : IAsyncDisposable
{
    /// <summary>Current life-cycle state.</summary>
    RelayHostState State { get; }

    /// <summary>The loopback port in use while <see cref="State"/> is Running; otherwise null.</summary>
    int? Port { get; }

    /// <summary>
    /// Raised for every <see cref="RelayEvent"/>. Handlers run on whichever thread produced the event
    /// (a request thread for <see cref="RelayEvent.PhoneSeen"/>), so they must be quick, must not throw
    /// and must marshal to their own thread if they touch UI.
    /// </summary>
    event EventHandler<RelayEvent>? EventRaised;

    /// <summary>Binds the port and starts answering requests.</summary>
    /// <exception cref="InvalidOperationException">The relay is already running.</exception>
    /// <exception cref="RelayStartException">The port could not be bound.</exception>
    Task StartAsync(RelayOptions options, CancellationToken cancellationToken = default);

    /// <summary>Stops answering requests and releases the port. Does nothing if already stopped.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
