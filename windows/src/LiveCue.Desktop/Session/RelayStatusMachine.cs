using LiveCue.Relay;

namespace LiveCue.Desktop.Session;

/// <summary>
/// The relay status machine of architecture 3.12: host and relay events to <see cref="RelayUiState"/>,
/// exactly the table of W-design 5.4. It holds the state and the pause flag and nothing else; the
/// texts, the fault code and everything that is not a state belong to the session. Plain transitions,
/// no threads and no clock, so every row of the table can be tested by itself.
/// </summary>
internal sealed class RelayStatusMachine
{
    public RelayUiState State { get; private set; } = RelayUiState.Stopped;

    /// <summary>The relay refuses new requests. Reset when the relay ends.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>True in Ready, Paused, Thinking and RequestFailed.</summary>
    public bool IsRunning => IsRunningState(State);

    public static bool IsRunningState(RelayUiState state) =>
        state is RelayUiState.Ready or RelayUiState.Paused or RelayUiState.Thinking or RelayUiState.RequestFailed;

    /// <summary>A start began: the checks are running.</summary>
    public void CheckingStarted() => State = RelayUiState.Checking;

    /// <summary>
    /// The checks finished. A failure is the PrerequisiteFailed layout unless a relay is running (a
    /// "Check again" beside a running relay changes no state). A pass after a failure returns to
    /// Stopped; a pass during a start leaves the start where it is.
    /// </summary>
    public void PrerequisitesChecked(bool passed)
    {
        if (IsRunning || State == RelayUiState.Starting)
        {
            return;
        }

        if (!passed)
        {
            State = RelayUiState.PrerequisiteFailed;
        }
        else if (State == RelayUiState.PrerequisiteFailed)
        {
            State = RelayUiState.Stopped;
        }
    }

    /// <summary>The checks passed and a relay instance is about to start.</summary>
    public void RelayStarting() => State = RelayUiState.Starting;

    public void RelayStarted()
    {
        IsPaused = false;
        State = RelayUiState.Ready;
    }

    /// <summary>Pause or resume, confirmed by the relay. While thinking, the state changes when the request ends.</summary>
    public void AcceptingChanged(bool accepting)
    {
        IsPaused = !accepting;
        if (IsRunning && State != RelayUiState.Thinking)
        {
            State = RestingState;
        }
    }

    public void RequestStarted()
    {
        if (IsRunning)
        {
            State = RelayUiState.Thinking;
        }
    }

    public void RequestCompleted()
    {
        if (IsRunning)
        {
            State = RestingState;
        }
    }

    /// <summary>Stays until the next request, pause or resume.</summary>
    public void RequestFailed()
    {
        if (IsRunning)
        {
            State = RelayUiState.RequestFailed;
        }
    }

    /// <summary>A start fault, a fault while running, or a start the supervisor refused.</summary>
    public void Faulted()
    {
        IsPaused = false;
        State = RelayUiState.Faulted;
    }

    /// <summary>The relay ended. A requested stop is Stopped; after a fault the state stays Faulted.</summary>
    public void RelayStopped(RelayStopReason reason)
    {
        IsPaused = false;
        State = reason == RelayStopReason.Requested ? RelayUiState.Stopped : RelayUiState.Faulted;
    }

    /// <summary>A fault that has been dealt with while stopped (the stored pairing was reset).</summary>
    public void FaultCleared()
    {
        if (State == RelayUiState.Faulted)
        {
            State = RelayUiState.Stopped;
        }
    }

    private RelayUiState RestingState => IsPaused ? RelayUiState.Paused : RelayUiState.Ready;
}
