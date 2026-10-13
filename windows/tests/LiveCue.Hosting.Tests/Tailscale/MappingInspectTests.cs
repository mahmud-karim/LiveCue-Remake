using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// The standing warnings of an inspection (W-design 4.15): a mapping this application created and that
/// is still live, but that the settings in force no longer use. Each can be removed, by its own port and
/// path, because it is in the ledger.
/// </summary>
public sealed class MappingInspectTests
{
    [Fact]
    public async Task The_other_modes_mapping_and_one_at_an_old_port_are_stale()
    {
        var current = TailscaleFixtures.PrivateEntry();
        var oldPort = TailscaleFixtures.PrivateEntry(12443);
        var otherMode = TailscaleFixtures.PublicEntry();
        var gone = TailscaleFixtures.PrivateEntry(13443);
        var replaced = TailscaleFixtures.PrivateEntry(14443);
        var session = TailscaleFixtures.PrivateEntry(15443);
        OwnedMapping[] ledger = [current, oldPort, otherMode, gone, replaced, session];

        // Live: the current private mapping, the one left at the old private port, the public one;
        // somebody else's handler where "replaced" was; a foreground session where "session" was.
        var live = ServeDoc.New()
            .Proxy(11443, "/")
            .Proxy(12443, "/")
            .Proxy(10000, "/livecue").Proxy(10000, "/other", 3000).Funnel(10000)
            .Proxy(14443, "/", 3000)
            .Foreground("session-1", ServeDoc.New().Proxy(15443, "/"))
            .Json();

        // Private is selected: its own mapping at the configured port is no warning; the two others are.
        var privateMode = new MappingHarness(new RelaySettings { OwnedMappings = ledger }).Status(live);
        var inspection = await privateMode.InspectAsync();

        Assert.Equal(TailscaleReadFailure.None, inspection.StatusFailure);
        Assert.Equal([oldPort, otherMode], inspection.StaleOwned);
        Assert.Equal((MappingState.Present, true), (inspection.Private.State, inspection.Private.InLedger));
        Assert.Equal((MappingState.Present, true), (inspection.Public.State, inspection.Public.InLedger));
        Assert.Equal(privateMode.Clock.GetUtcNow(), inspection.At);

        // Public is selected: now the public mapping is the one in use, and both private ones are stale.
        var publicMode = new MappingHarness(new RelaySettings { ConnectionMode = ConnectionMode.Public, OwnedMappings = ledger }).Status(live);
        Assert.Equal([current, oldPort], (await publicMode.InspectAsync()).StaleOwned);

        // The private port was changed in the settings: the mapping at the port before is stale, and
        // the new port is free.
        var moved = new MappingHarness(new RelaySettings { PrivateHttpsPort = 16443, OwnedMappings = ledger }).Status(live);
        inspection = await moved.InspectAsync();
        Assert.Equal([current, oldPort, otherMode], inspection.StaleOwned);
        Assert.Equal((MappingState.Absent, 16443), (inspection.Private.State, inspection.Private.HttpsPort));

        // The relay port was changed: every mapping to the old relay port is stale, and the private
        // port is occupied by it until it is removed.
        var retargeted = new MappingHarness(new RelaySettings { RelayPort = 47851, OwnedMappings = ledger }).Status(live);
        inspection = await retargeted.InspectAsync();
        Assert.Equal([current, oldPort, otherMode], inspection.StaleOwned);
        Assert.Equal((MappingState.Blocked, MappingRefusal.PrivatePortOccupied), (inspection.Private.State, inspection.Private.BlockedBy));
        Assert.Empty(inspection.Exposure);

        // The public prefix was changed: the mapping at the old prefix is stale.
        var renamed = new MappingHarness(
            new RelaySettings { ConnectionMode = ConnectionMode.Public, PublicPathPrefix = "/cue", OwnedMappings = ledger }).Status(live);
        inspection = await renamed.InspectAsync();
        Assert.Equal([current, oldPort, otherMode], inspection.StaleOwned);
        Assert.Equal(MappingState.Absent, inspection.Public.State);

        // An entry is the same mapping whatever its time says (settings.json keeps it to the second),
        // and an entry that is listed twice is one warning.
        var again = oldPort with { CreatedAt = oldPort.CreatedAt.AddHours(5) };
        var doubled = new MappingHarness(new RelaySettings { OwnedMappings = [current with { CreatedAt = DateTimeOffset.UnixEpoch }, oldPort, again] }, validatingStore: false)
            .Status(live);
        inspection = await doubled.InspectAsync();
        Assert.Equal(MappingState.Present, inspection.Private.State);
        Assert.Equal([oldPort], inspection.StaleOwned);

        // Nothing of that was written, and the ledger is as it was: an inspection tidies nothing up.
        Assert.All(new[] { privateMode, publicMode, moved, retargeted, renamed, doubled }, harness =>
        {
            Assert.Empty(harness.Writes);
            Assert.Equal(0, harness.Store.Saves);
            Assert.Equal(1, harness.ServeStatusReads);
        });

        // Each warning has a "Remove" that runs the command of the entry's own port and path, not of
        // the settings; the mapping in use is left alone.
        ServeDoc Rest() => ServeDoc.New()
            .Proxy(11443, "/")
            .Proxy(10000, "/other", 3000).Funnel(10000)
            .Proxy(14443, "/", 3000)
            .Foreground("session-1", ServeDoc.New().Proxy(15443, "/"));
        var withoutOldPort = Rest().Proxy(10000, "/livecue").Json();
        var harnessForRemoval = new MappingHarness(new RelaySettings { OwnedMappings = ledger })
            .Status(live, live, withoutOldPort, withoutOldPort, Rest().Json());
        var stale = (await harnessForRemoval.InspectAsync()).StaleOwned;

        var first = await harnessForRemoval.ExecuteAsync(harnessForRemoval.Manager.PlanRemoval(stale[0], TailscaleFixtures.Host));
        var second = await harnessForRemoval.ExecuteAsync(harnessForRemoval.Manager.PlanRemoval(stale[1], TailscaleFixtures.Host));

        Assert.Equal((MappingOutcome.Done, false), (first.Outcome, first.OthersChanged));
        Assert.Equal((MappingOutcome.Done, false), (second.Outcome, second.OthersChanged));
        Assert.Equal(
            ["serve --https=12443 --set-path=/ off", "funnel --https=10000 --set-path=/livecue off"],
            harnessForRemoval.Writes);
        Assert.Equal([otherMode], first.After!.StaleOwned);
        Assert.Empty(second.After!.StaleOwned);
        Assert.Equal([current], harnessForRemoval.Ledger);
        Assert.Equal(MappingState.Present, second.After.Private.State);
    }
}
