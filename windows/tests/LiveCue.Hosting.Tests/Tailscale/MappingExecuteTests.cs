using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.Relay.Processes;
using LiveCue.TestSupport;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// What <c>ExecuteAsync</c> reports once a command ran (W-design 4.15, steps 5 to 10): the status is read
/// again, the result is verified, and only then is the ledger changed.
/// </summary>
public sealed class MappingExecuteTests
{
    private static readonly string[] ServeStatusRead = ["serve", "status", "--json"];

    [Fact]
    public async Task Apply_adds_the_ledger_entry_after_verification()
    {
        // Private: nothing before, our handler after.
        var harness = new MappingHarness().Status(ServeStatusFixtures.Empty, ServeStatusFixtures.PrivateHandler());
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1234));

        var result = await harness.ApplyAsync(ConnectionMode.Private);

        Assert.Equal(MappingOutcome.Done, result.Outcome);
        Assert.Equal(MappingRefusal.None, result.Refusal);
        Assert.False(result.OthersChanged);
        Assert.Equal(
            [ServeStatusRead, ["serve", "--bg", "--https=11443", "http://127.0.0.1:47841"], ServeStatusRead],
            harness.Tailscale.ArgumentLists);

        // The entry: what was created, and when, to the second.
        var entry = Assert.Single(harness.Ledger);
        Assert.Equal(
            new OwnedMapping(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841", ManualTimeProvider.DefaultStart.AddSeconds(1)),
            entry);
        Assert.Equal(1, harness.Store.Saves);
        Assert.Same(harness.Settings, result.Settings);
        Assert.Equal(MappingState.Present, result.After!.Private.State);
        Assert.True(result.After.Private.InLedger);
        Assert.Empty(result.After.StaleOwned);
        Assert.Equal([new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, entry)], result.After.Exposure);

        // Public: Funnel already on for the port, with a neighbour that stays as it was.
        var neighbour = ServeDoc.New().Proxy(10000, "/other", 3000).Funnel(10000).Json();
        var publicHarness = new MappingHarness(new RelaySettings { ConnectionMode = ConnectionMode.Public })
            .Status(neighbour, ServeStatusFixtures.FunnelWithMounts());

        var publicResult = await publicHarness.ApplyAsync(ConnectionMode.Public);

        Assert.Equal(MappingOutcome.Done, publicResult.Outcome);
        Assert.False(publicResult.OthersChanged);
        Assert.Equal(["funnel --bg --https=10000 --set-path=/livecue http://127.0.0.1:47841"], publicHarness.Writes);
        Assert.Equal(
            [new OwnedMapping(ConnectionMode.Public, 10000, "/livecue", "http://127.0.0.1:47841", ManualTimeProvider.DefaultStart)],
            publicHarness.Ledger);
        Assert.Equal(MappingState.Present, publicResult.After!.Public.State);
        Assert.True(publicResult.After.Exposure.Single().FunnelOn);
    }

    [Fact]
    public async Task Remove_deletes_it()
    {
        // The port held only ours: Tailscale removes the port's entries with the handler.
        var entry = TailscaleFixtures.PrivateEntry();
        var harness = new MappingHarness(new RelaySettings { OwnedMappings = [entry] })
            .Status(ServeStatusFixtures.PrivateHandler(), ServeStatusFixtures.Empty);

        var result = await harness.RemoveAsync(entry);

        Assert.Equal(MappingOutcome.Done, result.Outcome);
        Assert.False(result.OthersChanged);
        Assert.Equal(["serve --https=11443 --set-path=/ off"], harness.Writes);
        Assert.Empty(harness.Ledger);
        Assert.Equal(1, harness.Store.Saves);
        Assert.Equal(MappingState.Absent, result.After!.Private.State);
        Assert.Empty(result.After.Exposure);

        // A Public mapping beside a neighbour: only our mount goes.
        var publicEntry = TailscaleFixtures.PublicEntry();
        var publicHarness = new MappingHarness(new RelaySettings { OwnedMappings = [entry, publicEntry] })
            .Status(ServeDoc.New().Proxy(11443, "/").Proxy(10000, "/livecue").Proxy(10000, "/other", 3000).Funnel(10000).Json(),
                ServeDoc.New().Proxy(11443, "/").Proxy(10000, "/other", 3000).Funnel(10000).Json());

        var publicResult = await publicHarness.RemoveAsync(publicEntry);

        Assert.Equal(MappingOutcome.Done, publicResult.Outcome);
        Assert.False(publicResult.OthersChanged);
        Assert.Equal(["funnel --https=10000 --set-path=/livecue off"], publicHarness.Writes);
        Assert.Equal([entry], publicHarness.Ledger);
        Assert.Equal(MappingState.Present, publicResult.After!.Private.State);
        Assert.Equal(MappingState.Absent, publicResult.After.Public.State);
    }

    [Fact]
    public async Task A_failed_command()
    {
        // Apply: Tailscale refuses; nothing changed; the ledger is not touched.
        var harness = new MappingHarness().Status(ServeStatusFixtures.Empty, ServeStatusFixtures.Empty);
        harness.Tailscale.WriteExitCode = 1;

        var plan = harness.PlanApply(ConnectionMode.Private);
        var result = await harness.ExecuteAsync(plan);

        Assert.Equal(MappingOutcome.CommandFailed, result.Outcome);
        Assert.Equal("Tailscale HTTPS setup failed. Check that Tailscale is connected.", MappingTexts.Failure(result, plan));
        Assert.False(result.OthersChanged);
        Assert.Empty(harness.Ledger);
        Assert.Equal(0, harness.Store.Saves);
        Assert.Equal(MappingState.Absent, result.After!.Private.State);
        Assert.Single(harness.Writes);

        // Apply that failed and still left the handler behind: it works, and it is not claimed.
        var left = new MappingHarness().Status(ServeStatusFixtures.Empty, ServeStatusFixtures.PrivateHandler());
        left.Tailscale.WriteExitCode = 1;

        var leftResult = await left.ApplyAsync(ConnectionMode.Private);

        Assert.Equal(MappingOutcome.CommandFailed, leftResult.Outcome);
        Assert.Empty(left.Ledger);
        Assert.Equal(MappingState.PresentNotOwned, leftResult.After!.Private.State);
        Assert.Single(leftResult.After.UnownedExposure);

        // Removal: the handler is still there, and so is the entry.
        var entry = TailscaleFixtures.PrivateEntry();
        var removal = new MappingHarness(new RelaySettings { OwnedMappings = [entry] })
            .Status(ServeStatusFixtures.PrivateHandler(), ServeStatusFixtures.PrivateHandler());
        removal.Tailscale.WriteOutcome = ProcessOutcome.StartFailed;

        var removalPlan = removal.Manager.PlanRemoval(entry, null);
        var removalResult = await removal.ExecuteAsync(removalPlan);

        Assert.Equal(MappingOutcome.CommandFailed, removalResult.Outcome);
        Assert.Equal(
            "Tailscale could not remove the mapping. Check that Tailscale is connected.",
            MappingTexts.Failure(removalResult, removalPlan));
        Assert.Equal([entry], removal.Ledger);
        Assert.Equal(MappingState.Present, removalResult.After!.Private.State);
    }

    [Fact]
    public async Task A_success_that_is_not_in_the_status_is_not_verified()
    {
        // Exit code 0, and the handler is not there.
        var harness = new MappingHarness().Status(ServeStatusFixtures.Empty, ServeStatusFixtures.Empty);

        var plan = harness.PlanApply(ConnectionMode.Private);
        var result = await harness.ExecuteAsync(plan);

        Assert.Equal(MappingOutcome.NotVerified, result.Outcome);
        Assert.Equal(
            "Tailscale reported success, but the mapping is not in its configuration. Choose Check again.",
            MappingTexts.Failure(result, plan));
        Assert.Empty(harness.Ledger);
        Assert.Equal(0, harness.Store.Saves);
        Assert.Equal(MappingState.Absent, result.After!.Private.State);

        // Public: the handler is there, but Funnel is not on for the port.
        var publicHarness = new MappingHarness(new RelaySettings { ConnectionMode = ConnectionMode.Public })
            .Status(ServeStatusFixtures.Empty, ServeDoc.New().Proxy(10000, "/livecue").Json());

        var publicResult = await publicHarness.ApplyAsync(ConnectionMode.Public);

        Assert.Equal(MappingOutcome.NotVerified, publicResult.Outcome);
        Assert.Empty(publicHarness.Ledger);

        // A handler of the same port and mount with another target is not ours either.
        var other = new MappingHarness().Status(ServeStatusFixtures.Empty, ServeStatusFixtures.ForeignHandler(11443));
        Assert.Equal(MappingOutcome.NotVerified, (await other.ApplyAsync(ConnectionMode.Private)).Outcome);
        Assert.Empty(other.Ledger);

        // The status cannot be read afterwards: not verified, and the state is unknown, not "absent".
        var unread = new MappingHarness().Status(ServeStatusFixtures.Empty, "not json");

        var unreadResult = await unread.ApplyAsync(ConnectionMode.Private);

        Assert.Equal(MappingOutcome.NotVerified, unreadResult.Outcome);
        Assert.Empty(unread.Ledger);
        Assert.Equal(TailscaleReadFailure.Unparseable, unreadResult.After!.StatusFailure);
        Assert.Equal(MappingState.Unknown, unreadResult.After.Private.State);

        // Removal that reported success while the handler stays: the entry stays too.
        var entry = TailscaleFixtures.PrivateEntry();
        var removal = new MappingHarness(new RelaySettings { OwnedMappings = [entry] })
            .Status(ServeStatusFixtures.PrivateHandler(), ServeStatusFixtures.PrivateHandler());

        var removalPlan = removal.Manager.PlanRemoval(entry, null);
        var removalResult = await removal.ExecuteAsync(removalPlan);

        Assert.Equal(MappingOutcome.NotVerified, removalResult.Outcome);
        Assert.Equal(
            "Tailscale reported success, but LiveCue could not confirm that the mapping is gone. Choose Check again.",
            MappingTexts.Failure(removalResult, removalPlan));
        Assert.Equal([entry], removal.Ledger);
    }

    [Fact]
    public async Task A_failed_save_is_ledger_not_saved()
    {
        var harness = new MappingHarness().Status(ServeStatusFixtures.Empty, ServeStatusFixtures.PrivateHandler());
        harness.Store.SaveFails = true;

        var plan = harness.PlanApply(ConnectionMode.Private);
        var result = await harness.ExecuteAsync(plan);

        // The mapping exists and works; LiveCue does not call it its own.
        Assert.Equal(MappingOutcome.LedgerNotSaved, result.Outcome);
        Assert.Equal(
            "The mapping changed, but LiveCue could not save its settings. LiveCue will treat the mapping as not its own.",
            MappingTexts.Failure(result, plan));
        Assert.Empty(harness.Ledger);
        Assert.Empty(result.Settings.OwnedMappings);
        Assert.Equal(MappingState.PresentNotOwned, result.After!.Private.State);
        Assert.False(result.After.Private.InLedger);
        var row = Assert.Single(result.After.UnownedExposure);
        Assert.Equal("tailscale serve --https=11443 --set-path=/ off", MappingTexts.ManualRemoval(row));

        // Removal: the handler is gone; the entry that could not be deleted matches nothing any more.
        var entry = TailscaleFixtures.PrivateEntry();
        var removal = new MappingHarness(new RelaySettings { OwnedMappings = [entry] })
            .Status(ServeStatusFixtures.PrivateHandler(), ServeStatusFixtures.Empty, ServeStatusFixtures.Empty);
        removal.Store.SaveFails = true;

        var removalResult = await removal.RemoveAsync(entry);

        Assert.Equal(MappingOutcome.LedgerNotSaved, removalResult.Outcome);
        Assert.Equal([entry], removal.Ledger);
        Assert.Equal(MappingState.Absent, removalResult.After!.Private.State);
        Assert.Empty(removalResult.After.StaleOwned);

        // The next change of the ledger drops it.
        removal.Store.SaveFails = false;
        removal.Status(ServeStatusFixtures.Empty);
        var again = await removal.RemoveAsync(entry);
        Assert.Equal(MappingRefusal.NotPresent, again.Refusal);
        Assert.Empty(removal.Ledger);
    }

    [Fact]
    public async Task Every_entry_that_matches_no_live_handler_leaves_the_ledger_with_the_next_change()
    {
        // Two entries of mappings somebody removed by hand; one that is still there.
        var gone = TailscaleFixtures.PrivateEntry(httpsPort: 12443);
        var goneToo = TailscaleFixtures.PublicEntry(httpsPort: 8443);
        var kept = TailscaleFixtures.PublicEntry();
        var settings = new RelaySettings { OwnedMappings = [gone, kept, goneToo] };
        var harness = new MappingHarness(settings).Status(
            ServeDoc.New().Proxy(10000, "/livecue").Funnel(10000).Json(),
            ServeDoc.New().Proxy(10000, "/livecue").Funnel(10000).Proxy(11443, "/").Json());

        var result = await harness.ApplyAsync(ConnectionMode.Private);

        Assert.Equal(MappingOutcome.Done, result.Outcome);
        Assert.Equal(2, harness.Ledger.Count);
        Assert.Equal(kept, harness.Ledger[0]);
        Assert.Equal((ConnectionMode.Private, 11443), (harness.Ledger[1].Mode, harness.Ledger[1].HttpsPort));
        Assert.Equal([kept], result.After!.StaleOwned);
    }

    [Fact]
    public async Task One_command_runs_at_a_time_and_a_cancelled_command_is_thrown()
    {
        var harness = new MappingHarness().Status(ServeStatusFixtures.Empty);
        var plan = harness.PlanApply(ConnectionMode.Private);
        harness.Tailscale.Processes.Gate = new TaskCompletionSource();
        using var cancel = new CancellationTokenSource();

        var first = harness.Manager.ExecuteAsync(plan, cancel.Token);
        var second = harness.Manager.ExecuteAsync(plan, CancellationToken.None);
        await Task.Delay(50);

        // The first holds the turn at its first read; the second has not started a process yet.
        Assert.Single(harness.Tailscale.Processes.Requests);
        Assert.False(second.IsCompleted);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        harness.Tailscale.Processes.Gate.SetResult();
        var result = await second;
        Assert.Equal(MappingOutcome.NotVerified, result.Outcome);
        Assert.Single(harness.Writes);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Manager.ExecuteAsync(plan, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Manager.InspectAsync(harness.Settings, cancelled.Token));
    }
}
