using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.Relay.Processes;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// The exposure report (architecture 3.9, W-design 4.15, security test ST-08): what Tailscale forwards
/// to the relay port, read from the live serve status and never from the ledger, so that a handler
/// nobody recorded is still shown. The documents are this folder's own.
/// </summary>
public sealed class ExposureReportTests
{
    private static readonly RelaySettings PublicMode = new() { ConnectionMode = ConnectionMode.Public };

    [Fact]
    public async Task Every_handler_that_targets_the_relay_port_is_listed()
    {
        // (a) Funnel on, at the configured public port and prefix, and not in the ledger.
        var atConfigured = new MappingHarness(PublicMode).Status(ServeDoc.New().Proxy(10000, "/livecue").Funnel(10000).Json());
        var inspection = await atConfigured.InspectAsync();
        Assert.Equal([new ExposedHandler(10000, "/livecue", FunnelOn: true, Foreground: false, LedgerEntry: null)], inspection.Exposure);
        Assert.True(inspection.HasFunnelExposure);
        Assert.Single(inspection.UnownedExposure);
        Assert.Equal(MappingState.PresentNotOwned, inspection.Public.State);

        // (b) Funnel on, on another port and mount.
        var elsewhere = new MappingHarness().Status(ServeDoc.New().Proxy(8443, "/by-hand").Funnel(8443).Json());
        inspection = await elsewhere.InspectAsync();
        Assert.Equal([new ExposedHandler(8443, "/by-hand", FunnelOn: true, Foreground: false, LedgerEntry: null)], inspection.Exposure);
        Assert.True(inspection.HasFunnelExposure);
        Assert.Equal(MappingState.Absent, inspection.Private.State);

        // (c) On the private port, after Funnel was switched on there. The ledger knows a tailnet-only
        // mapping; what is live is a published one, so the row is not the ledger's.
        var privateEntry = TailscaleFixtures.PrivateEntry();
        var published = new MappingHarness(new RelaySettings { OwnedMappings = [privateEntry] })
            .Status(ServeDoc.New().Proxy(11443, "/").Funnel(11443).Json());
        inspection = await published.InspectAsync();
        Assert.Equal([new ExposedHandler(11443, "/", FunnelOn: true, Foreground: false, LedgerEntry: null)], inspection.Exposure);
        Assert.True(inspection.HasFunnelExposure);
        Assert.Single(inspection.UnownedExposure);

        // Everything at once, with what must not be listed: another target, another relay port, a
        // handler that is not a proxy. Sorted by port, then mount.
        var oldPort = TailscaleFixtures.PrivateEntry(12443);
        var publicEntry = TailscaleFixtures.PublicEntry(443, "/cue");
        var everything = ServeDoc.New()
            .Proxy(10000, "/livecue").Funnel(10000)
            .Proxy(8443, "/by-hand").Proxy(8443, "/a").Funnel(8443)
            .Proxy(11443, "/").Funnel(11443)
            .Proxy(12443, "/")
            .Proxy(443, "/cue").Proxy(443, "/", 8096).Proxy(443, "/next", 47851).Funnel(443)
            .Handler(443, "/text", "Text", "http://127.0.0.1:47841")
            .Handler(443, "/path", "Path", "http://127.0.0.1:47841")
            .Proxy(13443, "/", 3000)
            .Foreground("session-1", ServeDoc.New().Proxy(444, "/fg"))
            .Service("svc:example", ServeDoc.New().Proxy(445, "/svc").Funnel(445))
            .Json();
        var all = new MappingHarness(new RelaySettings { OwnedMappings = [privateEntry, oldPort, publicEntry] }).Status(everything);

        inspection = await all.InspectAsync();

        Assert.Equal(
            [
                new ExposedHandler(443, "/cue", FunnelOn: true, Foreground: false, publicEntry),
                new ExposedHandler(444, "/fg", FunnelOn: false, Foreground: true, LedgerEntry: null),
                new ExposedHandler(445, "/svc", FunnelOn: true, Foreground: true, LedgerEntry: null),
                new ExposedHandler(8443, "/a", FunnelOn: true, Foreground: false, LedgerEntry: null),
                new ExposedHandler(8443, "/by-hand", FunnelOn: true, Foreground: false, LedgerEntry: null),
                new ExposedHandler(10000, "/livecue", FunnelOn: true, Foreground: false, LedgerEntry: null),
                new ExposedHandler(11443, "/", FunnelOn: true, Foreground: false, LedgerEntry: null),
                new ExposedHandler(12443, "/", FunnelOn: false, Foreground: false, oldPort),
            ],
            inspection.Exposure);
        Assert.True(inspection.HasFunnelExposure);
        Assert.Equal(6, inspection.UnownedExposure.Count());

        // The rows with a ledger entry are the stale mappings, with "Remove"; a row without one has no
        // plan, only a command shown as text (or none, for a session or a service).
        Assert.Equal([oldPort, publicEntry], inspection.StaleOwned);
        Assert.Equal(
            [
                string.Empty,
                string.Empty,
                "tailscale funnel --https=8443 --set-path=/a off",
                "tailscale funnel --https=8443 --set-path=/by-hand off",
                "tailscale funnel --https=10000 --set-path=/livecue off",
                "tailscale funnel --https=11443 --set-path=/ off",
            ],
            inspection.UnownedExposure.Select(MappingTexts.ManualRemoval));

        // The report follows the relay port of the settings in force for the run.
        var otherRelay = new MappingHarness(new RelaySettings { RelayPort = 47851 }).Status(everything);
        Assert.Equal(
            [new ExposedHandler(443, "/next", FunnelOn: true, Foreground: false, LedgerEntry: null)],
            (await otherRelay.InspectAsync()).Exposure);

        // It comes from the status and not from the ledger: a ledger full of entries and no live
        // handler is no exposure; a tailnet-only mapping of ours is listed and is not a Funnel exposure.
        var ledgerOnly = new MappingHarness(new RelaySettings { OwnedMappings = [privateEntry, oldPort, publicEntry] }).Status(ServeStatusFixtures.Empty);
        inspection = await ledgerOnly.InspectAsync();
        Assert.Empty(inspection.Exposure);
        Assert.False(inspection.HasFunnelExposure);
        Assert.Equal(TailscaleReadFailure.None, inspection.StatusFailure);

        var tailnetOnly = new MappingHarness(new RelaySettings { OwnedMappings = [privateEntry] }).Status(ServeStatusFixtures.PrivateHandler());
        inspection = await tailnetOnly.InspectAsync();
        Assert.Equal([new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, privateEntry)], inspection.Exposure);
        Assert.False(inspection.HasFunnelExposure);
        Assert.Empty(inspection.UnownedExposure);

        // Reading the report wrote nothing and changed no ledger.
        Assert.All(new[] { atConfigured, elsewhere, published, all, otherRelay, ledgerOnly, tailnetOnly }, harness =>
        {
            Assert.Empty(harness.Writes);
            Assert.Equal(0, harness.Store.Saves);
        });
    }

    [Fact]
    public async Task An_unreadable_status_is_unknown_not_empty()
    {
        var entry = TailscaleFixtures.PublicEntry();
        var cases = new List<(Action<MappingHarness> Arrange, TailscaleReadFailure Failure)>
        {
            (harness => harness.Tailscale.ReadExitCode = 1, TailscaleReadFailure.Failed),
            (harness => harness.Tailscale.ReadOutcome = ProcessOutcome.StartFailed, TailscaleReadFailure.Failed),
            (harness => harness.Tailscale.ReadOutcome = ProcessOutcome.TimedOut, TailscaleReadFailure.TimedOut),
            (harness => harness.Tailscale.ReadOutcome = ProcessOutcome.OutputLimitExceeded, TailscaleReadFailure.Unparseable),
            (harness => harness.Status(ServeStatusFixtures.NotAnObject), TailscaleReadFailure.Unparseable),
            (harness => harness.Status("null"), TailscaleReadFailure.Unparseable),
            (harness => harness.Status(string.Empty), TailscaleReadFailure.Unparseable),

            // A document that is cut off in the middle of the one handler that matters.
            (harness => harness.Status("""{"Web":{"pc.example.test:10000":{"Handlers":{"/livecue":{"Proxy":"http://127.0.0.1:47841"}"""), TailscaleReadFailure.Unparseable),

            // One member of an unexpected shape beside a handler that points at the relay: nothing is
            // concluded from the part that could be read.
            (harness => harness.Status("""{"Web":{"pc.example.test:10000":{"Handlers":{"/livecue":{"Proxy":"http://127.0.0.1:47841"}}}},"AllowFunnel":["pc.example.test:10000"]}"""), TailscaleReadFailure.Unparseable),
        };

        foreach (var (arrange, failure) in cases)
        {
            // The ledger says a public mapping exists. That is not what is reported either.
            var harness = new MappingHarness(new RelaySettings { ConnectionMode = ConnectionMode.Public, OwnedMappings = [entry] });
            arrange(harness);

            var inspection = await harness.InspectAsync();

            Assert.Equal(failure, inspection.StatusFailure);
            Assert.NotEqual(TailscaleReadFailure.None, inspection.StatusFailure);
            Assert.Empty(inspection.Exposure);
            Assert.False(inspection.HasFunnelExposure);
            Assert.Empty(inspection.UnownedExposure);
            Assert.Empty(inspection.StaleOwned);
            Assert.Equal(MappingState.Unknown, inspection.Private.State);
            Assert.Equal(MappingState.Unknown, inspection.Public.State);
            Assert.Equal([entry], harness.Ledger);
            Assert.Empty(harness.Writes);
        }

        // Without Tailscale the answer is the same kind of "unknown".
        var missing = await new MappingHarness(installed: false).InspectAsync();
        Assert.Equal(TailscaleReadFailure.NotInstalled, missing.StatusFailure);
        Assert.Empty(missing.Exposure);

        // "Nothing is exposed" is only ever said of a status that was read.
        var nothing = await new MappingHarness().Status(ServeStatusFixtures.Empty).InspectAsync();
        Assert.Equal(TailscaleReadFailure.None, nothing.StatusFailure);
        Assert.Empty(nothing.Exposure);
        Assert.Equal(MappingState.Absent, nothing.Private.State);

        Assert.Equal(
            "Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed. Choose Check again.",
            MappingTexts.ExposureUnknown);
    }
}
