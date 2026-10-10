using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.Relay.Processes;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// Every reason for which the manager runs nothing (architecture 3.9, rules TS-4, TS-5, TS-6 and TS-8,
/// and the remaining refusals of W-design 4.15). Each case checks the inspection, which the window shows
/// before any click, and the execution, which must refuse even when a plan is confirmed anyway.
/// </summary>
public sealed class MappingRefusalTests
{
    private static readonly RelaySettings PublicMode = new() { ConnectionMode = ConnectionMode.Public };

    // ---- TS-4 ----

    [Fact]
    public async Task Private_port_occupied()
    {
        string[] occupied =
        [
            ServeStatusFixtures.ForeignHandler(11443),                       // another program at the root
            ServeDoc.New().Proxy(11443, "/elsewhere", 3000).Json(),          // another program at another mount
            ServeDoc.New().Handler(11443, "/", "Text", "hello").Json(),      // not a proxy
            ServeDoc.New().Proxy(11443, "/", 47841, host: "old-name.example.test").Proxy(11443, "/x", 3000).Json(),
            ServeStatusFixtures.TcpForward(11443),                           // raw TCP
            ServeStatusFixtures.ForegroundSession(11443),                    // a foreground session
            ServeStatusFixtures.Service(11443),                              // a service
            ServeDoc.New().Foreground("session-1", ServeDoc.New().Proxy(11443, "/")).Json(), // ours by shape, but not in the background
            ServeDoc.New().Funnel(11443, on: false).Json(),                  // only mentioned
        ];

        foreach (var document in occupied.Take(3).Concat(occupied.Skip(4)))
        {
            await AssertBlockedAsync(new MappingHarness(), ConnectionMode.Private, document, MappingRefusal.PrivatePortOccupied);
        }

        // The fourth: our handler is there beside a neighbour. It is present, so there is nothing to apply.
        var shared = new MappingHarness().Status(occupied[3]);
        Assert.Equal(MappingState.PresentNotOwned, (await shared.InspectAsync()).Private.State);
    }

    [Fact]
    public async Task Private_port_with_funnel()
    {
        // "serve" on a port that has Funnel on would switch Funnel off for the whole port.
        string[] published =
        [
            ServeDoc.New().Proxy(11443, "/", 3000).Funnel(11443).Json(),
            ServeDoc.New().Proxy(11443, "/files", 3000).Funnel(11443).Json(),
            ServeDoc.New().Funnel(11443).Json(),
            ServeDoc.New().Foreground("session-1", ServeDoc.New().Proxy(11443, "/", 3000).Funnel(11443)).Json(),
        ];

        foreach (var document in published)
        {
            await AssertBlockedAsync(new MappingHarness(), ConnectionMode.Private, document, MappingRefusal.PrivatePortHasFunnel);
        }

        Assert.Equal(
            "Port 11443 has Funnel turned on. A private listener there would turn Funnel off for the whole port. Choose another private HTTPS port.",
            MappingTexts.Refusal(MappingRefusal.PrivatePortHasFunnel, 11443, "/"));
    }

    [Fact]
    public async Task Private_port_holding_only_ours_is_present()
    {
        // Not in the ledger: present, usable, not ours to remove.
        var unowned = new MappingHarness().Status(ServeStatusFixtures.PrivateHandler());
        var inspection = await unowned.InspectAsync();
        Assert.Equal(MappingState.PresentNotOwned, inspection.Private.State);
        Assert.False(inspection.Private.InLedger);
        Assert.Equal(MappingRefusal.None, inspection.Private.BlockedBy);
        Assert.Equal((11443, "/"), (inspection.Private.HttpsPort, inspection.Private.Path));

        // In the ledger: present.
        var owned = new MappingHarness(new RelaySettings { OwnedMappings = [TailscaleFixtures.PrivateEntry()] })
            .Status(ServeStatusFixtures.PrivateHandler());
        inspection = await owned.InspectAsync();
        Assert.Equal(MappingState.Present, inspection.Private.State);
        Assert.True(inspection.Private.InLedger);

        // Either way an apply runs nothing.
        foreach (var harness in new[] { unowned, owned })
        {
            var result = await harness.ApplyAsync(ConnectionMode.Private);
            Assert.Equal((MappingOutcome.Refused, MappingRefusal.AlreadyPresent), (result.Outcome, result.Refusal));
            Assert.Empty(harness.Writes);
        }
    }

    // ---- TS-5 ----

    [Fact]
    public async Task Public_mount_occupied()
    {
        string[] occupied =
        [
            ServeDoc.New().Proxy(10000, "/livecue", 3000).Funnel(10000).Json(),               // the prefix, another target
            ServeDoc.New().Proxy(10000, "/livecue/", 3000).Funnel(10000).Json(),              // the prefix with a slash
            ServeDoc.New().Proxy(10000, "/livecue/", 47841).Funnel(10000).Json(),             // even with our target
            ServeDoc.New().Handler(10000, "/livecue", "Path", @"C:\Sites\livecue").Funnel(10000).Json(),
            ServeDoc.New().Proxy(10000, "/livecue", 47841).Proxy(10000, "/livecue/", 3000).Json(),
            ServeDoc.New().Proxy(10000, "/livecue", 3000).Json(),                             // and with Funnel off
        ];

        foreach (var document in occupied)
        {
            await AssertBlockedAsync(new MappingHarness(PublicMode), ConnectionMode.Public, document, MappingRefusal.PublicMountOccupied);
        }

        // Another mount on the port does not occupy ours.
        var free = new MappingHarness(PublicMode).Status(ServeDoc.New().Proxy(10000, "/livecue-2", 3000).Funnel(10000).Json());
        Assert.Equal(MappingState.Absent, (await free.InspectAsync()).Public.State);
        Assert.Equal(
            "The path /livecue on port 10000 is already used by something else. Choose another path.",
            MappingTexts.Refusal(MappingRefusal.PublicMountOccupied, 10000, "/livecue"));
    }

    [Fact]
    public async Task Public_port_with_tcp_forward()
    {
        await AssertBlockedAsync(
            new MappingHarness(PublicMode), ConnectionMode.Public, ServeStatusFixtures.TcpForward(10000), MappingRefusal.PublicPortHasTcpForward);
        await AssertBlockedAsync(
            new MappingHarness(PublicMode),
            ConnectionMode.Public,
            ServeDoc.New().Service("svc:example", ServeDoc.New().TcpForward(10000)).Funnel(10000).Json(),
            MappingRefusal.PublicPortHasTcpForward);
    }

    [Fact]
    public async Task Public_port_would_publish_others()
    {
        // Tailnet-only handlers of others are on the port and Funnel is off: Funnel would publish them.
        string[] tailnetOnly =
        [
            ServeStatusFixtures.ForeignHandler(10000),
            ServeDoc.New().Proxy(10000, "/files", 3000).Json(),
            ServeDoc.New().Proxy(10000, "/livecue").Proxy(10000, "/files", 3000).Json(),   // ours beside one of others
            ServeDoc.New().Proxy(10000, "/files", 3000).Funnel(10000, on: false).Json(),
            ServeStatusFixtures.ForegroundSession(10000),
            ServeStatusFixtures.Service(10000),
        ];

        foreach (var document in tailnetOnly)
        {
            await AssertBlockedAsync(new MappingHarness(PublicMode), ConnectionMode.Public, document, MappingRefusal.PublicPortWouldPublishOthers);
        }

        // The same holds for the other two Funnel ports and for the fixture of a whole port.
        var on443 = new MappingHarness(PublicMode with { PublicHttpsPort = 443 });
        await AssertBlockedAsync(on443, ConnectionMode.Public, TailscaleFixtures.TailnetOnly443, MappingRefusal.PublicPortWouldPublishOthers);
    }

    [Fact]
    public async Task Public_port_with_funnel_on_is_allowed()
    {
        // The port is published already; our mount adds nothing of anybody else to the internet.
        var harness = new MappingHarness(PublicMode with { PublicHttpsPort = 443 }).Status(
            TailscaleFixtures.FunnelRootAndMount443,
            TailscaleFixtures.FunnelRootAndMount443,
            ServeDoc.New().Proxy(443, "/", 8096).Proxy(443, "/files", 5000).Proxy(443, "/livecue").Funnel(443).Json());

        var inspection = await harness.InspectAsync();
        Assert.Equal(MappingState.Absent, inspection.Public.State);
        Assert.Equal(MappingRefusal.None, inspection.Public.BlockedBy);

        var result = await harness.ApplyAsync(ConnectionMode.Public);

        Assert.Equal(MappingOutcome.Done, result.Outcome);
        Assert.False(result.OthersChanged);
        Assert.Equal(["funnel --bg --https=443 --set-path=/livecue http://127.0.0.1:47841"], harness.Writes);
        Assert.Equal(MappingState.Present, result.After!.Public.State);
    }

    [Fact]
    public async Task Public_port_unoccupied_is_allowed()
    {
        // Other ports do not matter: 10000 is free.
        var others = ServeDoc.New().Proxy(443, "/", 3000).Proxy(443, "/api/", 8080).TcpForward(5432);
        var before = others.Json();
        var harness = new MappingHarness(PublicMode).Status(before, before, others.Proxy(10000, "/livecue").Funnel(10000).Json());

        Assert.Equal(MappingState.Absent, (await harness.InspectAsync()).Public.State);
        var result = await harness.ApplyAsync(ConnectionMode.Public);

        Assert.Equal(MappingOutcome.Done, result.Outcome);
        Assert.False(result.OthersChanged);
        Assert.Equal(["funnel --bg --https=10000 --set-path=/livecue http://127.0.0.1:47841"], harness.Writes);
        Assert.Equal(TailscaleFixtures.PublicEntry() with { CreatedAt = harness.Ledger[0].CreatedAt }, Assert.Single(harness.Ledger));

        // A port that holds only our handler, tailnet-only, counts as unoccupied: Funnel publishes only ours.
        var onlyOurs = new MappingHarness(PublicMode).Status(ServeDoc.New().Proxy(10000, "/livecue").Json());
        Assert.Equal(MappingState.Absent, (await onlyOurs.InspectAsync()).Public.State);
    }

    // ---- TS-6 ----

    [Fact]
    public async Task Relay_port_47831_is_refused_before_any_command()
    {
        // The store of this harness accepts what no settings file could hold.
        var oldPort = new RelaySettings { RelayPort = 47831 };
        var harness = new MappingHarness(validatingStore: false).Status(ServeStatusFixtures.Empty);

        // No plan exists for it.
        foreach (var mode in new[] { ConnectionMode.Private, ConnectionMode.Public })
        {
            Assert.Null(harness.Manager.PlanApply(mode, oldPort, TailscaleFixtures.Host, out var refusal));
            Assert.Equal(MappingRefusal.ForbiddenRelayPort, refusal);
        }

        // A plan that was made while the settings were good is refused once they are not.
        var plan = harness.PlanApply(ConnectionMode.Private);
        harness.Store.Save(oldPort);
        var result = await harness.ExecuteAsync(plan);

        Assert.Equal((MappingOutcome.Refused, MappingRefusal.ForbiddenRelayPort), (result.Outcome, result.Refusal));
        Assert.Empty(harness.Writes);
        Assert.Equal(
            "Port 47831 belongs to the old LiveCue and is never used. Choose another relay port.",
            MappingTexts.Failure(result, plan));

        // The old LiveCue's own mapping is never "ours", never an exposure of this relay and never removable,
        // even with a ledger that claims it.
        var claimed = new OwnedMapping(ConnectionMode.Private, 443, "/", "http://127.0.0.1:47831", DateTimeOffset.UnixEpoch);
        var old = new MappingHarness(oldPort with { PrivateHttpsPort = 12443, OwnedMappings = [claimed] }, validatingStore: false)
            .Status(ServeDoc.New().Proxy(443, "/", 47831).Proxy(12443, "/", 47831).Json());

        var inspection = await old.InspectAsync();
        Assert.Equal((MappingState.Blocked, MappingRefusal.ForbiddenRelayPort), (inspection.Private.State, inspection.Private.BlockedBy));
        Assert.Equal((MappingState.Blocked, MappingRefusal.ForbiddenRelayPort), (inspection.Public.State, inspection.Public.BlockedBy));
        Assert.Empty(inspection.StaleOwned);
        Assert.Empty(inspection.Exposure);

        var removal = await old.RemoveAsync(claimed);
        Assert.Equal((MappingOutcome.Refused, MappingRefusal.NotInLedger), (removal.Outcome, removal.Refusal));
        Assert.Empty(old.Writes);
    }

    // ---- TS-8 ----

    [Fact]
    public async Task A_handler_not_in_the_ledger_is_present_and_cannot_be_planned_for_removal()
    {
        var harness = new MappingHarness().Status(ServeStatusFixtures.PrivateHandler());

        var inspection = await harness.InspectAsync();

        // Present and usable; nothing in the inspection can be handed to PlanRemoval.
        Assert.Equal(MappingState.PresentNotOwned, inspection.Private.State);
        Assert.Empty(inspection.StaleOwned);
        var row = Assert.Single(inspection.Exposure);
        Assert.Null(row.LedgerEntry);
        Assert.Equal("This mapping was not created by LiveCue, so LiveCue will not remove it.", MappingTexts.Refusal(MappingRefusal.NotInLedger, 11443, "/"));

        // A plan made anyway, from an entry the ledger does not hold, is refused.
        var result = await harness.RemoveAsync(TailscaleFixtures.PrivateEntry());

        Assert.Equal((MappingOutcome.Refused, MappingRefusal.PlanOutOfDate), (result.Outcome, result.Refusal));
        Assert.Empty(harness.Writes);
        Assert.Equal(0, harness.Store.Saves);
        Assert.Equal(MappingState.PresentNotOwned, result.After!.Private.State);
    }

    [Fact]
    public async Task A_ledger_entry_that_differs_from_the_live_handler_is_refused()
    {
        var privateEntry = TailscaleFixtures.PrivateEntry();
        var publicEntry = TailscaleFixtures.PublicEntry();
        (OwnedMapping Entry, string Document)[] cases =
        [
            (privateEntry, ServeStatusFixtures.ForeignHandler(11443)),                                  // another target
            (privateEntry, ServeStatusFixtures.PrivateHandler(relayPort: 47851)),                       // another relay port
            (privateEntry, ServeDoc.New().Handler(11443, "/", "Text", "http://127.0.0.1:47841").Json()), // another kind
            (privateEntry, ServeDoc.New().Handler(11443, "/", "Proxy", "http://localhost:47841").Json()), // another spelling
            (privateEntry, ServeDoc.New().Proxy(11443, "/").Proxy(11443, "/", 3000, host: "old-name.example.test").Json()), // two at that mount
            (publicEntry, ServeDoc.New().Proxy(10000, "/livecue", 3000).Funnel(10000).Json()),
            (publicEntry, ServeDoc.New().Handler(10000, "/livecue", "Path", @"C:\Sites\livecue").Funnel(10000).Json()),
        ];

        foreach (var (entry, document) in cases)
        {
            var harness = new MappingHarness(new RelaySettings { OwnedMappings = [privateEntry, publicEntry] }).Status(document);

            var result = await harness.RemoveAsync(entry);

            Assert.Equal((MappingOutcome.Refused, MappingRefusal.LedgerMismatch), (result.Outcome, result.Refusal));
            Assert.Empty(harness.Writes);
            Assert.Equal(0, harness.Store.Saves);
            Assert.Equal([privateEntry, publicEntry], harness.Ledger);
            Assert.DoesNotContain(entry, result.After!.StaleOwned);
        }

        Assert.Equal(
            "The mapping no longer matches what LiveCue created, so LiveCue will not remove it.",
            MappingTexts.Refusal(MappingRefusal.LedgerMismatch, 11443, "/"));
    }

    [Fact]
    public async Task A_vanished_handler_is_refused_and_its_entry_dropped()
    {
        var privateEntry = TailscaleFixtures.PrivateEntry();
        var publicEntry = TailscaleFixtures.PublicEntry();
        var harness = new MappingHarness(new RelaySettings { OwnedMappings = [privateEntry, publicEntry] })
            .Status(ServeStatusFixtures.FunnelWithMounts());

        var plan = harness.Manager.PlanRemoval(privateEntry, null);
        var result = await harness.ExecuteAsync(plan);

        Assert.Equal((MappingOutcome.Refused, MappingRefusal.NotPresent), (result.Outcome, result.Refusal));
        Assert.Equal("That mapping no longer exists in Tailscale. LiveCue removed it from its own list.", MappingTexts.Failure(result, plan));
        Assert.Empty(harness.Writes);
        Assert.Equal([publicEntry], harness.Ledger);
        Assert.Equal([publicEntry], result.Settings.OwnedMappings);
        Assert.Equal(1, harness.Store.Saves);

        // Only a foreground session is at that port and path now: ours is gone all the same.
        var foreground = new MappingHarness(new RelaySettings { OwnedMappings = [privateEntry] })
            .Status(ServeDoc.New().Foreground("session-1", ServeDoc.New().Proxy(11443, "/")).Json());

        var foregroundResult = await foreground.RemoveAsync(privateEntry);

        Assert.Equal(MappingRefusal.NotPresent, foregroundResult.Refusal);
        Assert.Empty(foreground.Writes);
        Assert.Empty(foreground.Ledger);
    }

    [Fact]
    public async Task No_removal_plan_exists_for_a_handler_that_is_not_in_the_ledger()
    {
        // A Funnel handler made by hand at the configured public port and prefix, and one elsewhere.
        var harness = new MappingHarness(PublicMode).Status(
            ServeDoc.New().Proxy(10000, "/livecue").Funnel(10000).Proxy(8443, "/by-hand").Funnel(8443).Json());

        var inspection = await harness.InspectAsync();

        // Shown, with the command as text; no ledger entry, so nothing to plan a removal from.
        Assert.Equal(MappingState.PresentNotOwned, inspection.Public.State);
        Assert.Equal(2, inspection.UnownedExposure.Count());
        Assert.All(inspection.Exposure, row => Assert.Null(row.LedgerEntry));
        Assert.Empty(inspection.StaleOwned);
        Assert.Equal(
            ["tailscale funnel --https=8443 --set-path=/by-hand off", "tailscale funnel --https=10000 --set-path=/livecue off"],
            inspection.Exposure.Select(MappingTexts.ManualRemoval));

        // The manager takes plans, and plans are made from ledger entries: no member of it accepts a
        // handler, an exposure row or a text.
        var parameters = typeof(IMappingManager).GetMethods().SelectMany(method => method.GetParameters()).Select(parameter => parameter.ParameterType);
        Assert.DoesNotContain(typeof(ExposedHandler), parameters);
        Assert.DoesNotContain(typeof(ServeHandler), parameters);
        Assert.DoesNotContain(typeof(MappingCommand), parameters);
        Assert.Equal(typeof(string), typeof(MappingTexts).GetMethod(nameof(MappingTexts.ManualRemoval))!.ReturnType);

        // Plans forged from entries that describe those handlers exactly are refused: the ledger is empty.
        foreach (var forged in new[] { TailscaleFixtures.PublicEntry(), TailscaleFixtures.PublicEntry(8443, "/by-hand") })
        {
            var result = await harness.RemoveAsync(forged);

            Assert.Equal((MappingOutcome.Refused, MappingRefusal.PlanOutOfDate), (result.Outcome, result.Refusal));
        }

        Assert.Empty(harness.Writes);
        Assert.Equal(0, harness.Store.Saves);
    }

    // ---- The remaining refusals ----

    [Fact]
    public async Task Not_installed()
    {
        var harness = new MappingHarness(installed: false);

        var inspection = await harness.InspectAsync();
        var result = await harness.ApplyAsync(ConnectionMode.Private);

        Assert.Equal(TailscaleReadFailure.NotInstalled, inspection.StatusFailure);
        Assert.Equal((MappingState.Unknown, MappingRefusal.TailscaleNotInstalled), (inspection.Private.State, inspection.Private.BlockedBy));
        Assert.Equal((MappingState.Unknown, MappingRefusal.TailscaleNotInstalled), (inspection.Public.State, inspection.Public.BlockedBy));
        Assert.Empty(inspection.Exposure);
        Assert.Equal((MappingOutcome.Refused, MappingRefusal.TailscaleNotInstalled), (result.Outcome, result.Refusal));
        Assert.Null(result.After);
        Assert.Equal("Tailscale is not installed in Program Files.", MappingTexts.Refusal(result.Refusal, 11443, "/"));

        // Nothing was started at all, not even a read.
        Assert.Empty(harness.Tailscale.Processes.Requests);

        // An executable that is there and cannot be started: the status is unavailable, and nothing is written.
        var unstartable = new MappingHarness();
        unstartable.Tailscale.ReadOutcome = ProcessOutcome.StartFailed;
        Assert.Equal(MappingRefusal.StatusUnavailable, (await unstartable.ApplyAsync(ConnectionMode.Private)).Refusal);
        Assert.Empty(unstartable.Writes);
    }

    [Fact]
    public async Task Invalid_settings()
    {
        var harness = new MappingHarness(validatingStore: false).Status(ServeStatusFixtures.Empty);
        RelaySettings[] invalid =
        [
            RelaySettings.Default with { RelayPort = 80 },
            RelaySettings.Default with { RelayPort = 65536 },
            RelaySettings.Default with { RelayPort = RelaySettings.Default.AsrLabPort },
            RelaySettings.Default with { PrivateHttpsPort = 0 },
            RelaySettings.Default with { PrivateHttpsPort = 65536 },
            RelaySettings.Default with { PrivateHttpsPort = 443 },      // a port Funnel can use
            RelaySettings.Default with { PrivateHttpsPort = 8443 },
            RelaySettings.Default with { PrivateHttpsPort = 10000, PublicHttpsPort = 443 },
            RelaySettings.Default with { PublicHttpsPort = 444 },
            RelaySettings.Default with { PublicHttpsPort = 11443 },
            RelaySettings.Default with { PublicPathPrefix = "/" },
            RelaySettings.Default with { PublicPathPrefix = "/Live Cue" },
            RelaySettings.Default with { PublicPathPrefix = "/livecue\n" },
            RelaySettings.Default with { PublicPathPrefix = "/x --yes" },
            RelaySettings.Default with { PublicPathPrefix = null! },
        ];

        var good = harness.PlanApply(ConnectionMode.Private);
        foreach (var settings in invalid)
        {
            // The plan of the mode that the bad value belongs to never exists.
            var privatePlan = harness.Manager.PlanApply(ConnectionMode.Private, settings, null, out var privateRefusal);
            var publicPlan = harness.Manager.PlanApply(ConnectionMode.Public, settings, null, out var publicRefusal);
            Assert.True(privatePlan is null || publicPlan is null);
            Assert.Contains(MappingRefusal.InvalidSettings, new[] { privateRefusal, publicRefusal });
            Assert.DoesNotContain(MappingRefusal.ForbiddenRelayPort, new[] { privateRefusal, publicRefusal });

            // Saved (by a store that does not validate), they stop a plan that was good a moment ago.
            if (privatePlan is null)
            {
                harness.Store.Save(settings);
                var result = await harness.ExecuteAsync(good);
                Assert.Equal((MappingOutcome.Refused, MappingRefusal.InvalidSettings), (result.Outcome, result.Refusal));
                Assert.Equal(MappingState.Blocked, result.After!.Private.State);
                Assert.Equal(MappingRefusal.InvalidSettings, result.After.Private.BlockedBy);
            }
        }

        Assert.Empty(harness.Writes);
        Assert.Equal(
            "The connection settings are not valid. Check the ports and the path, then save.",
            MappingTexts.Refusal(MappingRefusal.InvalidSettings, 11443, "/"));

        // A mode that is neither of the two has no command.
        Assert.Null(harness.Manager.PlanApply((ConnectionMode)7, RelaySettings.Default, null, out var unknownMode));
        Assert.Equal(MappingRefusal.InvalidSettings, unknownMode);
    }

    [Fact]
    public async Task Settings_that_a_save_would_reject_stop_an_apply_before_it_runs()
    {
        // The Codex path was valid when it was saved and the file is gone now: the ledger entry of a new
        // mapping could not be recorded, so the mapping is not created.
        var settings = new RelaySettings { CodexExecutablePath = @"C:\Tools\Codex\codex.exe" };
        var harness = new MappingHarness(settings).Status(ServeStatusFixtures.Empty);

        var result = await harness.ApplyAsync(ConnectionMode.Private);

        Assert.Equal((MappingOutcome.Refused, MappingRefusal.InvalidSettings), (result.Outcome, result.Refusal));
        Assert.Empty(harness.Writes);

        // A removal is not held back by that: taking a mapping away must stay possible.
        var entry = TailscaleFixtures.PrivateEntry();
        var removal = new MappingHarness(settings with { OwnedMappings = [entry] })
            .Status(ServeStatusFixtures.PrivateHandler(), ServeStatusFixtures.Empty);

        var removed = await removal.RemoveAsync(entry);

        Assert.Equal(MappingOutcome.LedgerNotSaved, removed.Outcome);
        Assert.Equal(["serve --https=11443 --set-path=/ off"], removal.Writes);
    }

    [Fact]
    public async Task Already_present()
    {
        var publicEntry = TailscaleFixtures.PublicEntry();
        (RelaySettings Settings, ConnectionMode Mode, string Document)[] cases =
        [
            (RelaySettings.Default, ConnectionMode.Private, ServeStatusFixtures.PrivateHandler()),
            (PublicMode, ConnectionMode.Public, ServeStatusFixtures.FunnelWithMounts()),
            (PublicMode with { OwnedMappings = [publicEntry] }, ConnectionMode.Public, ServeStatusFixtures.FunnelWithMounts()),
            (PublicMode, ConnectionMode.Public, TailscaleFixtures.SharedPortsAndTheRelay),
        ];

        foreach (var (settings, mode, document) in cases)
        {
            var harness = new MappingHarness(settings).Status(document);

            var plan = harness.PlanApply(mode);
            var result = await harness.ExecuteAsync(plan);

            Assert.Equal((MappingOutcome.Refused, MappingRefusal.AlreadyPresent), (result.Outcome, result.Refusal));
            Assert.Equal("The connection is already set up.", MappingTexts.Failure(result, plan));
            Assert.Empty(harness.Writes);
            Assert.Equal(0, harness.Store.Saves);
        }
    }

    [Fact]
    public async Task Plan_out_of_date()
    {
        // The dialog was opened for port 11443; the settings were saved with another port meanwhile.
        var harness = new MappingHarness().Status(ServeStatusFixtures.Empty);
        var plan = harness.PlanApply(ConnectionMode.Private);
        harness.Store.Save(RelaySettings.Default with { PrivateHttpsPort = 12443 });

        var result = await harness.ExecuteAsync(plan);

        Assert.Equal((MappingOutcome.Refused, MappingRefusal.PlanOutOfDate), (result.Outcome, result.Refusal));
        Assert.Equal(
            "The settings changed while this dialog was open. Close it and look at the command again.",
            MappingTexts.Failure(result, plan));
        Assert.Empty(harness.Writes);

        // The same for the relay port, the public port and the prefix.
        foreach (var changed in new[]
            {
                PublicMode with { RelayPort = 47851 },
                PublicMode with { PublicHttpsPort = 8443 },
                PublicMode with { PublicPathPrefix = "/cue" },
            })
        {
            var publicHarness = new MappingHarness(PublicMode).Status(ServeStatusFixtures.Empty);
            var publicPlan = publicHarness.PlanApply(ConnectionMode.Public);
            publicHarness.Store.Save(changed);

            Assert.Equal(MappingRefusal.PlanOutOfDate, (await publicHarness.ExecuteAsync(publicPlan)).Refusal);
            Assert.Empty(publicHarness.Writes);
        }

        // A removal whose entry left the ledger while the dialog was open.
        var entry = TailscaleFixtures.PrivateEntry();
        var removal = new MappingHarness(new RelaySettings { OwnedMappings = [entry] }).Status(ServeStatusFixtures.PrivateHandler());
        var removalPlan = removal.Manager.PlanRemoval(entry, null);
        removal.Store.Save(RelaySettings.Default);

        Assert.Equal(MappingRefusal.PlanOutOfDate, (await removal.ExecuteAsync(removalPlan)).Refusal);
        Assert.Empty(removal.Writes);

        // A plan put together by hand: the action of one form with the command of another, an apply with
        // an entry, a removal without one. None of them is a plan the manager made.
        var forged = new MappingHarness(new RelaySettings { OwnedMappings = [entry] }).Status(ServeStatusFixtures.PrivateHandler());
        var apply = forged.PlanApply(ConnectionMode.Private);
        var remove = forged.Manager.PlanRemoval(entry, null);
        MappingPlan[] plans =
        [
            apply with { Action = MappingAction.ApplyPublic },
            apply with { Command = remove.Command },
            apply with { Entry = entry },
            apply with { Command = null! },
            remove with { Action = MappingAction.RemovePublic },
            remove with { Command = apply.Command },
            remove with { Command = MappingCommand.RemovePrivate(12443) },
            remove with { Entry = null },
            remove with { Command = null! },
            remove with { Action = (MappingAction)9 },
        ];

        foreach (var forgedPlan in plans)
        {
            Assert.Equal(MappingRefusal.PlanOutOfDate, (await forged.ExecuteAsync(forgedPlan)).Refusal);
        }

        Assert.Empty(forged.Writes);
    }

    /// <summary>The inspection says Blocked with that reason, and a confirmed plan is refused with it and runs nothing.</summary>
    private static async Task AssertBlockedAsync(MappingHarness harness, ConnectionMode mode, string document, MappingRefusal expected)
    {
        harness.Status(document);

        var inspection = (await harness.InspectAsync()).For(mode);
        var result = await harness.ApplyAsync(mode);

        Assert.True(inspection.State == MappingState.Blocked && inspection.BlockedBy == expected, $"{inspection.State} {inspection.BlockedBy}: {document}");
        Assert.False(inspection.InLedger);
        Assert.Equal((MappingOutcome.Refused, expected), (result.Outcome, result.Refusal));
        Assert.NotEmpty(MappingTexts.Refusal(expected, inspection.HttpsPort, inspection.Path));
        Assert.Empty(harness.Writes);
        Assert.Equal(0, harness.Store.Saves);
        Assert.Equal(2, harness.ServeStatusReads);
    }
}
