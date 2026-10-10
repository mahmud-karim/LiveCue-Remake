namespace LiveCue.Relay;

/// <summary>
/// Something the relay tells its host. New kinds are added as nested records; hosts switch on the
/// type and ignore kinds they do not know.
/// </summary>
public abstract record RelayEvent
{
    private RelayEvent()
    {
    }

    /// <summary>The relay is listening on <paramref name="Port"/> on the loopback interface.</summary>
    public sealed record Started(int Port) : RelayEvent;

    /// <summary>The relay has stopped listening.</summary>
    public sealed record Stopped : RelayEvent;

    /// <summary>A request with an accepted pairing token arrived at <paramref name="At"/>.</summary>
    public sealed record PhoneSeen(DateTimeOffset At) : RelayEvent;
}
