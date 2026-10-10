using LiveCue.Relay;
using LiveCue.Relay.Processes;

namespace LiveCue.Hosting;

/// <summary>Creates the real relay, with the host's clock and process runner (W-design 4.16).</summary>
internal sealed class ProductionRelayFactory : IRelayFactory
{
    private readonly TimeProvider _clock;
    private readonly IProcessRunner _processes;

    public ProductionRelayFactory(TimeProvider clock, IProcessRunner processes)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(processes);
        _clock = clock;
        _processes = processes;
    }

    public IRelay Create(RelayOptions options) =>
        RelayFactory.Create(options, new RelayDependencies { Clock = _clock, Processes = _processes });
}
