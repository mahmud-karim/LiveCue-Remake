using LiveCue.Hosting.Tailscale;
using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// The real <see cref="TailscaleCli"/> and <see cref="MappingManager"/> over <see cref="FakeTailscale"/>:
/// a process runner that starts nothing, records every argument list and plays the status documents of
/// a scenario. No test in this folder runs a Tailscale command, read or write.
/// </summary>
internal sealed class MappingHarness
{
    /// <param name="settings">What the store holds at the start.</param>
    /// <param name="installed">False: there is no executable anywhere.</param>
    /// <param name="validatingStore">False: the store accepts anything, so a test can plant settings and
    /// a ledger that no validated file could hold.</param>
    public MappingHarness(RelaySettings? settings = null, bool installed = true, bool validatingStore = true)
    {
        Tailscale = new FakeTailscale(installed);
        Validator = new SettingsValidator(Tailscale.Files);
        Store = new InMemorySettingsStore(settings, validatingStore ? Validator : null);
        Cli = new TailscaleCli(Tailscale.Processes, Tailscale.Files);
        Manager = new MappingManager(Cli, Store, Validator, Clock);
    }

    public FakeTailscale Tailscale { get; }

    public SettingsValidator Validator { get; }

    public InMemorySettingsStore Store { get; }

    public ManualTimeProvider Clock { get; } = new();

    public TailscaleCli Cli { get; }

    public MappingManager Manager { get; }

    public RelaySettings Settings => Store.Stored;

    public IReadOnlyList<OwnedMapping> Ledger => Store.Stored.OwnedMappings;

    /// <summary>The write commands that reached the "executable", each joined by single spaces.</summary>
    public IReadOnlyList<string> Writes => [.. Tailscale.WriteCommands.Select(arguments => string.Join(' ', arguments))];

    /// <summary>How often the serve status was read.</summary>
    public int ServeStatusReads => Tailscale.ArgumentLists.Count(arguments => arguments.SequenceEqual(["serve", "status", "--json"]));

    /// <summary>Queues the serve status documents the next reads return; the last one repeats.</summary>
    public MappingHarness Status(params string[] documents)
    {
        Tailscale.ServeStatus(documents);
        return this;
    }

    public Task<ConnectionInspection> InspectAsync() => Manager.InspectAsync(Settings, CancellationToken.None);

    /// <summary>The apply plan of a mode from the stored settings, as the Settings page asks for it.</summary>
    public MappingPlan PlanApply(ConnectionMode mode)
    {
        var plan = Manager.PlanApply(mode, Settings, TailscaleFixtures.Host, out var refusal);
        Assert.Equal(MappingRefusal.None, refusal);
        return plan!;
    }

    public Task<MappingResult> ApplyAsync(ConnectionMode mode) => ExecuteAsync(PlanApply(mode));

    public Task<MappingResult> RemoveAsync(OwnedMapping entry) =>
        ExecuteAsync(Manager.PlanRemoval(entry, TailscaleFixtures.Host));

    public Task<MappingResult> ExecuteAsync(MappingPlan plan) => Manager.ExecuteAsync(plan, CancellationToken.None);
}
