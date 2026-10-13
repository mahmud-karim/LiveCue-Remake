using LiveCue.Hosting.Tailscale;
using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// Rule TS-7 (W-design 4.15, step 9): after a command the serve status is compared with the one read
/// before it. A difference in an entry that is not ours is reported; our own handler, and the entries of
/// a port we had to ourselves, are not; and nothing is ever put back.
/// </summary>
public sealed class SnapshotDiffTests
{
    private static readonly string[] ServeStatusRead = ["serve", "status", "--json"];

    [Fact]
    public async Task A_foreign_change_is_reported()
    {
        // The command did what was asked and nothing else: no report.
        var clean = new MappingHarness().Status(Neighbours().Json(), Neighbours().Proxy(11443, "/").Json());
        Assert.False((await clean.ApplyAsync(ConnectionMode.Private)).OthersChanged);

        // Our handler appeared, and something of somebody else's is not as it was.
        (string What, string After)[] foreign =
        [
            ("a neighbour's target", Neighbours().Proxy(443, "/files", 5001).Proxy(11443, "/").Json()),
            ("a neighbour's handler is gone", ServeDoc.New().Proxy(443, "/", 8096).Funnel(443).Proxy(8443, "/", 9000).Proxy(11443, "/").Json()),
            ("a neighbour's kind", Neighbours().Handler(443, "/files", "Text", "http://127.0.0.1:5000").Proxy(11443, "/").Json()),
            ("Funnel switched off elsewhere", Neighbours().Funnel(443, on: false).Proxy(11443, "/").Json()),
            ("Funnel switched on elsewhere", Neighbours().Funnel(8443).Proxy(11443, "/").Json()),
            ("a new handler elsewhere", Neighbours().Proxy(8443, "/new", 9001).Proxy(11443, "/").Json()),
            ("a new port", Neighbours().Proxy(12443, "/", 9001).Proxy(11443, "/").Json()),
            ("raw TCP elsewhere", Neighbours().TcpForward(5432).Proxy(11443, "/").Json()),
            ("a foreground session", Neighbours().Foreground("session-1", ServeDoc.New().Proxy(444, "/", 9001)).Proxy(11443, "/").Json()),
            ("a service", Neighbours().Service("svc:example", ServeDoc.New().Proxy(445, "/", 9001)).Proxy(11443, "/").Json()),
            ("a member this reader does not know", Neighbours().Member("Future", "value").Proxy(11443, "/").Json()),
        ];

        foreach (var (what, after) in foreign)
        {
            var harness = new MappingHarness().Status(Neighbours().Json(), after);

            var result = await harness.ApplyAsync(ConnectionMode.Private);

            Assert.True(result.OthersChanged, what);
            Assert.Equal(MappingOutcome.Done, result.Outcome);
        }

        // A port that is shared is not ours as a whole: Funnel going off for it, or a neighbour's mount
        // going with ours, is a foreign change even though our own removal succeeded.
        var entry = TailscaleFixtures.PublicEntry();
        var shared = ServeDoc.New().Proxy(10000, "/livecue").Proxy(10000, "/other", 3000).Funnel(10000).Json();
        (string What, string After)[] onOurPort =
        [
            ("Funnel went off for the shared port", ServeDoc.New().Proxy(10000, "/other", 3000).Funnel(10000, on: false).Json()),
            ("the Funnel entry of the shared port is gone", ServeDoc.New().Proxy(10000, "/other", 3000).Json()),
            ("the neighbour's mount went too", ServeDoc.New().Funnel(10000).Json()),
            ("the neighbour's mount changed", ServeDoc.New().Proxy(10000, "/other", 3001).Funnel(10000).Json()),
        ];

        foreach (var (what, after) in onOurPort)
        {
            var harness = new MappingHarness(new RelaySettings { OwnedMappings = [entry] }).Status(shared, after);

            var result = await harness.RemoveAsync(entry);

            Assert.True(result.OthersChanged, what);
            Assert.Equal(MappingOutcome.Done, result.Outcome);
            Assert.Empty(harness.Ledger);
        }

        // It is reported whatever the command itself said: failed, timed out, or not verified.
        var failed = new MappingHarness().Status(Neighbours().Json(), ServeDoc.New().Proxy(443, "/", 8096).Json());
        failed.Tailscale.WriteExitCode = 1;
        var failedResult = await failed.ApplyAsync(ConnectionMode.Private);
        Assert.Equal((MappingOutcome.CommandFailed, true), (failedResult.Outcome, failedResult.OthersChanged));

        var unverified = new MappingHarness().Status(Neighbours().Json(), ServeStatusFixtures.Empty);
        var unverifiedResult = await unverified.ApplyAsync(ConnectionMode.Private);
        Assert.Equal((MappingOutcome.NotVerified, true), (unverifiedResult.Outcome, unverifiedResult.OthersChanged));

        Assert.Equal(
            "Warning: other entries of Tailscale's serve configuration changed during this command. LiveCue repaired nothing. Review them with: tailscale serve status",
            MappingTexts.OthersChanged);
    }

    [Fact]
    public async Task Only_our_handler_and_its_port_entries_are_ignored()
    {
        const string Web = "/Web/pc.example.test:10000";
        var before = Snapshot(Neighbours().Json());

        // A port that was unoccupied: Tailscale adds the handler, the TCP entry, the web server and the
        // Funnel entry together. All four are ours.
        var added = Snapshot(Neighbours().Proxy(10000, "/livecue").Funnel(10000).Json());
        Assert.Empty(SnapshotDiff.ChangedPointers(before, added, 10000, "/livecue", portWasOursAlone: true));
        Assert.False(SnapshotDiff.OthersChanged(before, added, 10000, "/livecue", portWasOursAlone: true));
        Assert.False(SnapshotDiff.OthersChanged(added, before, 10000, "/livecue", portWasOursAlone: true));

        // The same difference on a port that was not ours alone: only the handler itself is ours.
        Assert.Equal(
            ["/AllowFunnel/pc.example.test:10000", "/TCP/10000/HTTPS"],
            SnapshotDiff.ChangedPointers(before, added, 10000, "/livecue", portWasOursAlone: false));
        Assert.True(SnapshotDiff.OthersChanged(before, added, 10000, "/livecue", portWasOursAlone: false));

        // A shared Funnel port: our mount comes and goes beside the neighbour, and nothing else moves.
        var sharedBefore = Snapshot(Neighbours().Proxy(10000, "/other", 3000).Funnel(10000).Json());
        var sharedAfter = Snapshot(Neighbours().Proxy(10000, "/other", 3000).Funnel(10000).Proxy(10000, "/livecue").Json());
        Assert.False(SnapshotDiff.OthersChanged(sharedBefore, sharedAfter, 10000, "/livecue", portWasOursAlone: false));
        Assert.False(SnapshotDiff.OthersChanged(sharedAfter, sharedBefore, 10000, "/livecue", portWasOursAlone: false));

        // What is not ignored on that shared port, pointer by pointer.
        (string After, string[] Changed)[] notOurs =
        [
            // Another mount, also the one that differs from ours by a final slash or a further segment.
            (Shared().Proxy(10000, "/livecue").Proxy(10000, "/livecue/", 3000).Json(), [Web + "/Handlers/~1livecue~1/Proxy"]),
            (Shared().Proxy(10000, "/livecue").Proxy(10000, "/livecue/x", 3000).Json(), [Web + "/Handlers/~1livecue~1x/Proxy"]),
            (Shared().Proxy(10000, "/livecue").Proxy(10000, "/other", 3001).Json(), [Web + "/Handlers/~1other/Proxy"]),

            // The port's own entries.
            (Shared().Proxy(10000, "/livecue").Funnel(10000, on: false).Json(), ["/AllowFunnel/pc.example.test:10000"]),
            (Shared().Proxy(10000, "/livecue").TcpForward(10000).Json(), ["/TCP/10000/HTTPS", "/TCP/10000/TCPForward"]),

            // Our port and mount in a structure that is not the background configuration.
            (Shared().Proxy(10000, "/livecue").Foreground("session-1", ServeDoc.New().Proxy(10000, "/livecue")).Json(),
                ["/Foreground/session-1/TCP/10000/HTTPS", "/Foreground/session-1" + Web + "/Handlers/~1livecue/Proxy"]),

            // Our mount on another port.
            (Shared().Proxy(10000, "/livecue").Proxy(8443, "/livecue").Json(), ["/Web/pc.example.test:8443/Handlers/~1livecue/Proxy"]),
        ];

        foreach (var (after, changed) in notOurs)
        {
            Assert.Equal(changed, SnapshotDiff.ChangedPointers(sharedBefore, Snapshot(after), 10000, "/livecue", portWasOursAlone: false));
            Assert.True(SnapshotDiff.OthersChanged(sharedBefore, Snapshot(after), 10000, "/livecue", portWasOursAlone: false));
        }

        // Having a port to ourselves covers that port and no other, and never a session or a service.
        var elsewhere = Snapshot(Neighbours().Proxy(10000, "/livecue").Funnel(10000).Funnel(8443).Json());
        Assert.Equal(
            ["/AllowFunnel/pc.example.test:8443"],
            SnapshotDiff.ChangedPointers(before, elsewhere, 10000, "/livecue", portWasOursAlone: true));
        var session = Snapshot(Neighbours().Proxy(10000, "/livecue").Funnel(10000)
            .Foreground("session-1", ServeDoc.New().Funnel(10000)).Json());
        Assert.Equal(
            ["/Foreground/session-1/AllowFunnel/pc.example.test:10000"],
            SnapshotDiff.ChangedPointers(before, session, 10000, "/livecue", portWasOursAlone: true));

        // A map that is empty holds nobody's entry: it is the same as its absence.
        Assert.False(SnapshotDiff.OthersChanged(
            Snapshot(ServeDoc.New().Proxy(11443, "/").Json()), Snapshot("""{"TCP":{},"Web":{},"AllowFunnel":{}}"""), 11443, "/", portWasOursAlone: true));
        Assert.False(SnapshotDiff.OthersChanged(Snapshot("{}"), Snapshot("""{"Web":{}}"""), 11443, "/", portWasOursAlone: true));

        // And through the manager, which decides "ours alone" from the status read before the command.
        var alone = new MappingHarness(new RelaySettings { ConnectionMode = ConnectionMode.Public })
            .Status(Neighbours().Json(), Neighbours().Proxy(10000, "/livecue").Funnel(10000).Json());
        Assert.False((await alone.ApplyAsync(ConnectionMode.Public)).OthersChanged);

        var entry = TailscaleFixtures.PublicEntry();
        var leaving = new MappingHarness(new RelaySettings { OwnedMappings = [entry] })
            .Status(Neighbours().Proxy(10000, "/livecue").Funnel(10000).Json(), Neighbours().Json());
        var left = await leaving.RemoveAsync(entry);
        Assert.Equal((MappingOutcome.Done, false), (left.Outcome, left.OthersChanged));

        var beside = new MappingHarness(new RelaySettings { ConnectionMode = ConnectionMode.Public })
            .Status(Shared().Json(), Shared().Proxy(10000, "/livecue").Json());
        Assert.False((await beside.ApplyAsync(ConnectionMode.Public)).OthersChanged);
    }

    [Fact]
    public async Task Nothing_is_repaired()
    {
        // The command took a neighbour's handler with it and switched Funnel off for the neighbour's port.
        var before = Neighbours().Json();
        var after = ServeDoc.New().Proxy(443, "/", 8096).Funnel(443, on: false).Proxy(8443, "/", 9000).Proxy(11443, "/").Json();
        var harness = new MappingHarness().Status(before, after);

        var result = await harness.ApplyAsync(ConnectionMode.Private);

        // It is said, and that is all: one read, the one confirmed command, one read.
        Assert.True(result.OthersChanged);
        Assert.Equal(MappingOutcome.Done, result.Outcome);
        Assert.Equal(
            [ServeStatusRead, ["serve", "--bg", "--https=11443", "http://127.0.0.1:47841"], ServeStatusRead],
            harness.Tailscale.ArgumentLists);
        Assert.Equal([new OwnedMapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841", harness.Ledger[0].CreatedAt)], harness.Ledger);

        // Looking again changes nothing either, and the removal of our own mapping is the only command
        // the application will make from here: the neighbour's entries stay as the command left them.
        await harness.InspectAsync();
        await harness.InspectAsync();
        Assert.Single(harness.Writes);
        Assert.Equal([MappingCommandKind.RemovePrivate], PlannableRemovals(harness, await harness.InspectAsync()));

        // The same after a removal that damaged a neighbour.
        var entry = TailscaleFixtures.PublicEntry();
        var removal = new MappingHarness(new RelaySettings { OwnedMappings = [entry] }).Status(
            ServeDoc.New().Proxy(10000, "/livecue").Proxy(10000, "/other", 3000).Funnel(10000).Json(),
            ServeDoc.New().Funnel(10000).Json());

        var removed = await removal.RemoveAsync(entry);

        Assert.True(removed.OthersChanged);
        Assert.Equal(
            [ServeStatusRead, ["funnel", "--https=10000", "--set-path=/livecue", "off"], ServeStatusRead],
            removal.Tailscale.ArgumentLists);
        Assert.Empty(PlannableRemovals(removal, removed.After!));

        // The comparison is a pure function of two snapshots: it is handed nothing it could run.
        var parameters = typeof(SnapshotDiff)
            .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();
        Assert.DoesNotContain(typeof(ITailscaleCli), parameters);
        Assert.DoesNotContain(typeof(MappingCommand), parameters);
        Assert.Empty(typeof(SnapshotDiff).GetFields(
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
    }

    /// <summary>Programs of others: two on a published 443 and one, tailnet only, on 8443.</summary>
    private static ServeDoc Neighbours() =>
        ServeDoc.New().Proxy(443, "/", 8096).Proxy(443, "/files", 5000).Funnel(443).Proxy(8443, "/", 9000);

    /// <summary>The neighbours, and a published 10000 that already carries a mount of somebody else.</summary>
    private static ServeDoc Shared() => Neighbours().Proxy(10000, "/other", 3000).Funnel(10000);

    private static ServeSnapshot Snapshot(string json)
    {
        Assert.True(ServeStatusParser.TryParse(json, out var snapshot), json);
        return snapshot;
    }

    /// <summary>The removal commands an inspection leaves the window able to ask for: those of ledger entries only.</summary>
    private static List<MappingCommandKind> PlannableRemovals(MappingHarness harness, ConnectionInspection inspection) =>
        [.. harness.Ledger.Concat(inspection.StaleOwned).Distinct().Select(entry => harness.Manager.PlanRemoval(entry, null).Command.Kind)];
}
