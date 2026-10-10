namespace LiveCue.Relay.Core;

/// <summary>Pause and shutdown, as every area observes them.</summary>
internal interface IAccessState
{
    /// <summary>False while paused.</summary>
    bool Accepting { get; }

    /// <summary>Cancelled when the relay next stops accepting. While paused: already cancelled.
    /// A fresh token is issued on resume.</summary>
    CancellationToken WhileAccepting { get; }

    /// <summary>Cancelled when StopAsync begins or a fatal fault is raised. Never reset.</summary>
    CancellationToken Stopping { get; }
}
