using LiveCue.Relay.Processes;

namespace LiveCue.Relay.Core;

/// <summary>What every area receives at construction. One per relay instance.</summary>
internal sealed class RelayContext
{
    public RelayContext(
        RelayOptions options,
        RelayDependencies dependencies,
        IRelayEventSink events,
        IAccessState access,
        IProcessRunner processes,
        string advertisedEndpoint)
    {
        Options = options;
        Dependencies = dependencies;
        Events = events;
        Access = access;
        Processes = processes;
        AdvertisedEndpoint = advertisedEndpoint;
    }

    public RelayOptions Options { get; }

    public RelayDependencies Dependencies { get; }

    public TimeProvider Clock => Dependencies.Clock;

    public IRelayEventSink Events { get; }

    public IAccessState Access { get; }

    /// <summary>Dependencies.Processes, or the production runner.</summary>
    public IProcessRunner Processes { get; }

    /// <summary>Options.AdvertisedEndpoint in canonical form.</summary>
    public string AdvertisedEndpoint { get; }

    /// <summary>Dependencies.TempRoot, or the OS temp folder.</summary>
    public string TempRoot => Dependencies.TempRoot ?? Path.GetTempPath();
}
