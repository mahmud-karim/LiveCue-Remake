using System.Globalization;
using System.Text.RegularExpressions;
using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.Relay.Processes;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// The safety rules of architecture 3.9 that hold whatever the settings, the ledger and Tailscale's own
/// status say: TS-1 (no write without a status that was read), TS-2 (validated values only), TS-3 (the
/// four command forms and nothing else), TS-9 (reading and planning never write) and TS-10 (the limit).
/// Everything runs over a process runner that starts nothing and records each argument list.
/// </summary>
public sealed partial class MappingSafetyTests
{
    private static readonly string[] StatusRead = ["status", "--json"];
    private static readonly string[] ServeStatusRead = ["serve", "status", "--json"];
    private static readonly int[] FunnelPorts = [443, 8443, 10000];

    // ---- TS-1 ----

    [Fact]
    public async Task A_failed_timed_out_or_unparseable_status_writes_nothing()
    {
        var entry = TailscaleFixtures.PrivateEntry();
        var cases = new List<(Action<MappingHarness> Arrange, TailscaleReadFailure Failure, MappingRefusal Refusal)>
        {
            (harness => harness.Tailscale.ReadExitCode = 1, TailscaleReadFailure.Failed, MappingRefusal.StatusUnavailable),
            (harness => harness.Tailscale.ReadOutcome = ProcessOutcome.StartFailed, TailscaleReadFailure.Failed, MappingRefusal.StatusUnavailable),
            (harness => harness.Tailscale.ReadOutcome = ProcessOutcome.TimedOut, TailscaleReadFailure.TimedOut, MappingRefusal.StatusUnavailable),
            (harness => harness.Tailscale.ReadOutcome = ProcessOutcome.OutputLimitExceeded, TailscaleReadFailure.Unparseable, MappingRefusal.StatusUnreadable),
        };
        cases.AddRange(TailscaleFixtures.Malformed.Select(document =>
            ((Action<MappingHarness>)(harness => harness.Status(document)), TailscaleReadFailure.Unparseable, MappingRefusal.StatusUnreadable)));

        foreach (var (arrange, failure, refusal) in cases)
        {
            var harness = new MappingHarness(new RelaySettings { OwnedMappings = [entry] });
            arrange(harness);

            var inspection = await harness.InspectAsync();
            var apply = await harness.ApplyAsync(ConnectionMode.Private);
            var publish = await harness.ApplyAsync(ConnectionMode.Public);
            var remove = await harness.RemoveAsync(entry);

            // The inspection says "unknown", for both modes and for what is exposed.
            Assert.Equal(failure, inspection.StatusFailure);
            Assert.Equal((MappingState.Unknown, refusal), (inspection.Private.State, inspection.Private.BlockedBy));
            Assert.Equal((MappingState.Unknown, refusal), (inspection.Public.State, inspection.Public.BlockedBy));
            Assert.Empty(inspection.StaleOwned);
            Assert.Empty(inspection.Exposure);

            // And nothing is written: no command, no ledger change.
            Assert.All(new[] { apply, publish, remove }, result =>
            {
                Assert.Equal((MappingOutcome.Refused, refusal), (result.Outcome, result.Refusal));
                Assert.False(result.OthersChanged);
                Assert.Null(result.After);
                Assert.Equal(
                    "LiveCue could not read Tailscale's configuration, so it changed nothing. Check that Tailscale is running, then choose Check again.",
                    MappingTexts.Refusal(result.Refusal, 11443, "/"));
            });
            Assert.Empty(harness.Writes);
            Assert.Equal(0, harness.Store.Saves);
            Assert.Equal([entry], harness.Ledger);
            Assert.All(harness.Tailscale.Processes.Requests, request => Assert.Equal(ServeStatusRead, request.Arguments));
        }
    }

    [Fact]
    public async Task Every_action_begins_with_the_serve_status()
    {
        var entry = TailscaleFixtures.PrivateEntry(httpsPort: 12443);
        var harness = new MappingHarness(new RelaySettings { OwnedMappings = [entry] }).Status(
            ServeDoc.New().Proxy(12443, "/").Json(),
            ServeDoc.New().Proxy(12443, "/").Proxy(11443, "/").Json(),
            ServeDoc.New().Proxy(12443, "/").Proxy(11443, "/").Json(),
            ServeDoc.New().Proxy(11443, "/").Json());

        await harness.ApplyAsync(ConnectionMode.Private);
        await harness.RemoveAsync(entry);

        // Read, write, read; read, write, read.
        Assert.Equal(
            ["serve status --json", "serve --bg --https=11443 http://127.0.0.1:47841", "serve status --json",
             "serve status --json", "serve --https=12443 --set-path=/ off", "serve status --json"],
            harness.Tailscale.ArgumentLists.Select(arguments => string.Join(' ', arguments)));
    }

    // ---- TS-2 ----

    [Fact]
    public async Task Only_validated_values_reach_a_command()
    {
        // Values that would do harm on a command line, in every place a value can come from.
        string[] prefixes =
        [
            "/livecue --yes", "/x off", "--set-path=/", "/", "//", "/..", "/a/b", "/Livecue", "/livecue\n", "\n/livecue", "/livecue\r\n",
            "/livecue\0", "/-reset", "/x;reset", "/x&reset", "/x|reset", "/\"x\"", "/x y", " /x", "/x ", "reset", "off", "--yes", "--tcp=443",
            "/" + new string('a', 33), string.Empty,
        ];
        int[] ports = [int.MinValue, -443, -1, 0, 65536, 99999, int.MaxValue];

        var harness = new MappingHarness(validatingStore: false).Status(ServeStatusFixtures.Empty);
        var goodPrivate = harness.PlanApply(ConnectionMode.Private);
        var goodPublic = harness.PlanApply(ConnectionMode.Public);

        var hostile = new List<RelaySettings>();
        hostile.AddRange(prefixes.Select(prefix => RelaySettings.Default with { PublicPathPrefix = prefix }));
        hostile.AddRange(ports.Select(port => RelaySettings.Default with { RelayPort = port }));
        hostile.AddRange(ports.Select(port => RelaySettings.Default with { PrivateHttpsPort = port }));
        hostile.AddRange(ports.Select(port => RelaySettings.Default with { PublicHttpsPort = port }));
        hostile.Add(RelaySettings.Default with { RelayPort = 47831 });

        foreach (var settings in hostile)
        {
            // No plan exists for them (the mode the value belongs to, at least), so no dialog can show one.
            var privatePlan = harness.Manager.PlanApply(ConnectionMode.Private, settings, "pc.example.test", out _);
            var publicPlan = harness.Manager.PlanApply(ConnectionMode.Public, settings, "pc.example.test", out _);
            Assert.True(privatePlan is null && publicPlan is null, Describe(settings));

            // Saved by a store that validates nothing, they stop the plans that were good a moment ago.
            harness.Store.Save(settings);
            Assert.Equal(MappingOutcome.Refused, (await harness.ExecuteAsync(goodPrivate)).Outcome);
            Assert.Equal(MappingOutcome.Refused, (await harness.ExecuteAsync(goodPublic)).Outcome);
        }

        // A ledger that was written by hand: entries no factory accepts have no plan, and the manager
        // says so by refusing to make one.
        OwnedMapping[] crafted =
        [
            .. prefixes.Select(prefix => TailscaleFixtures.PublicEntry() with { Path = prefix }),
            .. ports.Select(port => TailscaleFixtures.PublicEntry() with { HttpsPort = port }),
            .. ports.Select(port => TailscaleFixtures.PrivateEntry() with { HttpsPort = port }),
            TailscaleFixtures.PublicEntry(httpsPort: 11443),                       // Funnel cannot use that port
            TailscaleFixtures.PrivateEntry() with { Path = "/livecue" },           // a private mapping is at the root
            TailscaleFixtures.PrivateEntry() with { Path = "/ off" },
            TailscaleFixtures.PrivateEntry() with { Mode = (ConnectionMode)5 },
            TailscaleFixtures.PublicEntry() with { Path = null! },
        ];
        harness.Store.Save(RelaySettings.Default with { OwnedMappings = crafted });
        foreach (var entry in crafted)
        {
            Assert.Throws<ArgumentException>(() => harness.Manager.PlanRemoval(entry, "pc.example.test"));

            // Put into a plan with a well-formed command of the same kind, it is refused all the same.
            var borrowed = harness.Manager.PlanRemoval(TailscaleFixtures.PublicEntry(), null) with { Entry = entry };
            Assert.Equal(MappingOutcome.Refused, (await harness.ExecuteAsync(borrowed)).Outcome);
        }

        // Entries that a factory accepts but that this application cannot have written: a target that is
        // not the relay's. They are in the ledger and are still not ours.
        string[] targets =
        [
            "http://127.0.0.1:47831", "http://127.0.0.1:80", "http://127.0.0.1:047841", "http://127.0.0.1:47841/", "http://127.0.0.1:47841 --yes",
            "http://localhost:47841", "https://127.0.0.1:47841", "http://192.0.2.1:47841", "http://127.0.0.1:", "reset", string.Empty,
        ];
        foreach (var target in targets)
        {
            var entry = TailscaleFixtures.PrivateEntry() with { Target = target };
            var claimed = new MappingHarness(RelaySettings.Default with { OwnedMappings = [entry] }, validatingStore: false)
                .Status(ServeDoc.New().Handler(11443, "/", "Proxy", target).Json());

            var inspection = await claimed.InspectAsync();
            var result = await claimed.RemoveAsync(entry);

            Assert.Empty(inspection.StaleOwned);
            Assert.All(inspection.Exposure, row => Assert.Null(row.LedgerEntry));
            Assert.Equal((MappingOutcome.Refused, MappingRefusal.NotInLedger), (result.Outcome, result.Refusal));
            Assert.Empty(claimed.Writes);
        }

        // A list of arguments that was changed after the factory returned is refused; the plan is "out of date".
        harness.Store.Save(RelaySettings.Default);
        foreach (var (index, text) in new[] { (0, "funnel"), (1, "--yes"), (2, "--https=443"), (3, "http://127.0.0.1:47831"), (3, "reset") })
        {
            var tampered = harness.PlanApply(ConnectionMode.Private);
            ((string[])tampered.Command.Arguments)[index] = text;

            var result = await harness.ExecuteAsync(tampered);

            Assert.Equal((MappingOutcome.Refused, MappingRefusal.PlanOutOfDate), (result.Outcome, result.Refusal));
        }

        // Through all of that not one command was written.
        Assert.Empty(harness.Writes);

        // And when a command does run, it is the factory's own list for the saved values, text for text.
        var plan = harness.PlanApply(ConnectionMode.Public);
        ((string[])plan.Command.Arguments)[0] = new string("funnel".ToCharArray());
        await harness.ExecuteAsync(plan);
        Assert.Equal(
            MappingCommand.ApplyPublic(10000, "/livecue", 47841).Arguments,
            Assert.Single(harness.Tailscale.WriteCommands));
    }

    // ---- TS-3 ----

    [Fact]
    public async Task Across_generated_settings_every_argument_list_is_one_of_the_four_forms()
    {
        var random = new Random(20261010);
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var writes = 0;
        var refusals = 0;

        for (var round = 0; round < 4000; round++)
        {
            var settings = Generated.Settings(random);
            var harness = new MappingHarness(settings, installed: random.Next(40) != 0, validatingStore: false);
            harness.Tailscale.WriteOutcome = Generated.Pick(random, ProcessOutcome.Exited, ProcessOutcome.Exited, ProcessOutcome.Exited, ProcessOutcome.TimedOut, ProcessOutcome.StartFailed);
            harness.Tailscale.WriteExitCode = random.Next(5) == 0 ? 1 : 0;
            if (random.Next(25) == 0)
            {
                harness.Tailscale.ReadOutcome = Generated.Pick(random, ProcessOutcome.TimedOut, ProcessOutcome.StartFailed, ProcessOutcome.OutputLimitExceeded);
            }

            harness.Status([.. Enumerable.Range(0, 12).Select(_ => Generated.Document(random, settings))]);

            // Everything a window can ask of the manager, in a random order, with random plans.
            for (var step = 0; step < 6; step++)
            {
                var before = harness.Tailscale.WriteCommands.Count;
                var saved = harness.Settings;
                var plan = Generated.Plan(random, harness, saved);
                if (plan is null)
                {
                    await harness.Manager.InspectAsync(Generated.Pick(random, saved, Generated.Settings(random)), CancellationToken.None);
                    Assert.Equal(before, harness.Tailscale.WriteCommands.Count);
                    continue;
                }

                var result = await harness.ExecuteAsync(plan);

                // At most one command per confirmed plan, and none for a refusal.
                var written = harness.Tailscale.WriteCommands.Skip(before).ToList();
                if (result.Outcome == MappingOutcome.Refused)
                {
                    refusals++;
                    Assert.Empty(written);
                    continue;
                }

                // TS-3, last clause: it is the command of the configured port and path, and of no other.
                var arguments = Assert.Single(written);
                Assert.Equal(Expected(plan, saved), arguments);
                writes++;
                kinds[arguments[0] + " " + arguments[^1].Split(':')[0]] = kinds.GetValueOrDefault(arguments[0] + " " + arguments[^1].Split(':')[0]) + 1;

                if (random.Next(3) == 0)
                {
                    harness.Store.Save(Generated.Settings(random));
                }
            }

            await harness.Cli.ReadStatusAsync(CancellationToken.None);

            Assert.All(harness.Tailscale.Processes.Requests, request =>
            {
                Assert.Equal(FakeTailscale.ExecutablePath, request.FileName);
                AssertIsOneOfTheSixArgumentLists(request.Arguments);
            });
        }

        // The run is not vacuous: each of the four forms was written many times, and many plans were refused.
        // (With this seed: 2,049 commands written, 9,839 plans refused.)
        Assert.True(writes > 1500, $"{writes} commands were written.");
        Assert.True(refusals > 5000, $"{refusals} plans were refused.");
        Assert.Equal(["funnel http", "funnel off", "serve http", "serve off"], kinds.Keys.Order(StringComparer.Ordinal));
        Assert.All(kinds.Values, count => Assert.True(count > 200, string.Join(", ", kinds)));
    }

    // ---- TS-9 ----

    [Fact]
    public async Task Inspect_and_plan_never_write()
    {
        var ledger = new[] { TailscaleFixtures.PrivateEntry(), TailscaleFixtures.PublicEntry(), TailscaleFixtures.PrivateEntry(12443) };
        string[] documents =
        [
            TailscaleFixtures.NoHandlers, TailscaleFixtures.TailnetOnly443, TailscaleFixtures.FunnelRootAndMount443,
            TailscaleFixtures.SharedPortsAndTheRelay, ServeStatusFixtures.PrivateHandler(), ServeStatusFixtures.FunnelWithMounts(),
            ServeStatusFixtures.ForeignHandler(), ServeStatusFixtures.TcpForward(), ServeStatusFixtures.ForegroundSession(),
            ServeStatusFixtures.Service(), ServeStatusFixtures.NotAnObject,
            ServeDoc.New().Proxy(11443, "/").Proxy(12443, "/").Proxy(10000, "/livecue").Funnel(10000).Json(),
        ];

        foreach (var mode in new[] { ConnectionMode.Private, ConnectionMode.Public })
        {
            var settings = new RelaySettings { ConnectionMode = mode, OwnedMappings = ledger };
            var harness = new MappingHarness(settings).Status(documents);

            // Planning alone: no process, no file, no read of the settings.
            Assert.NotNull(harness.Manager.PlanApply(ConnectionMode.Private, settings, "pc.example.test", out _));
            Assert.NotNull(harness.Manager.PlanApply(ConnectionMode.Public, settings, null, out _));
            Assert.All(ledger, entry => Assert.NotNull(harness.Manager.PlanRemoval(entry, "pc.example.test")));
            Assert.Empty(harness.Tailscale.Processes.Requests);
            Assert.Equal(0, harness.Store.Loads);

            // Inspecting: one read of the serve status each time, whatever it finds, even a stale mapping
            // of ours or a handler that points at the relay. Nothing is tidied up on the way.
            foreach (var document in documents)
            {
                var inspection = await harness.InspectAsync();
                Assert.NotNull(inspection);
            }

            Assert.Equal(documents.Length, harness.Tailscale.ArgumentLists.Count);
            Assert.All(harness.Tailscale.ArgumentLists, arguments => Assert.Equal(ServeStatusRead, arguments));
            Assert.Empty(harness.Writes);
            Assert.Equal(0, harness.Store.Saves);
            Assert.Equal(ledger, harness.Ledger);
            Assert.Equal(0, harness.Tailscale.Files.Writes);
        }
    }

    // ---- TS-10 ----

    [Fact]
    public async Task A_write_that_times_out_is_reported_with_the_terminal_hint()
    {
        // The command did not finish in thirty seconds and changed nothing.
        var harness = new MappingHarness().Status(ServeStatusFixtures.Empty, ServeStatusFixtures.Empty);
        harness.Tailscale.WriteOutcome = ProcessOutcome.TimedOut;

        var plan = harness.PlanApply(ConnectionMode.Private);
        var result = await harness.ExecuteAsync(plan);

        Assert.Equal(MappingOutcome.CommandTimedOut, result.Outcome);
        Assert.Equal(
            "Tailscale did not finish in time. Run the command shown above in a terminal to see Tailscale's own instructions. The first use of HTTPS or Funnel on a tailnet must be approved in the Tailscale admin console.",
            MappingTexts.Failure(result, plan));
        Assert.Equal("tailscale serve --bg --https=11443 http://127.0.0.1:47841", plan.Command.DisplayText);
        Assert.Empty(harness.Ledger);
        Assert.Equal(MappingState.Absent, result.After!.Private.State);

        // It ran once, with the limit, and was not tried again.
        var write = Assert.Single(harness.Tailscale.Processes.Requests, request => !request.Arguments.SequenceEqual(ServeStatusRead));
        Assert.Equal(TimeSpan.FromSeconds(30), write.Timeout);
        Assert.Equal(0, write.MaxStandardOutputBytes);

        // The owner then runs the shown command in a terminal, or the command took effect after all:
        // the handler works, is shown as an exposure with its removal command, and is not claimed.
        var late = new MappingHarness(new RelaySettings { ConnectionMode = ConnectionMode.Public })
            .Status(ServeStatusFixtures.Empty, ServeDoc.New().Proxy(10000, "/livecue").Funnel(10000).Json());
        late.Tailscale.WriteOutcome = ProcessOutcome.TimedOut;

        var lateResult = await late.ApplyAsync(ConnectionMode.Public);

        Assert.Equal(MappingOutcome.CommandTimedOut, lateResult.Outcome);
        Assert.Empty(late.Ledger);
        Assert.Equal(MappingState.PresentNotOwned, lateResult.After!.Public.State);
        var row = Assert.Single(lateResult.After.UnownedExposure);
        Assert.True(lateResult.After.HasFunnelExposure);
        Assert.Equal("tailscale funnel --https=10000 --set-path=/livecue off", MappingTexts.ManualRemoval(row));

        // A removal that times out keeps its entry while the handler is still there.
        var entry = TailscaleFixtures.PublicEntry();
        var removal = new MappingHarness(new RelaySettings { OwnedMappings = [entry] })
            .Status(ServeStatusFixtures.FunnelWithMounts(), ServeStatusFixtures.FunnelWithMounts());
        removal.Tailscale.WriteOutcome = ProcessOutcome.TimedOut;

        Assert.Equal(MappingOutcome.CommandTimedOut, (await removal.RemoveAsync(entry)).Outcome);
        Assert.Equal([entry], removal.Ledger);
    }

    [GeneratedRegex(@"\Ahttp://127\.0\.0\.1:(?<port>[1-9][0-9]{3,4})\z", RegexOptions.CultureInvariant)]
    private static partial Regex RelayTarget();

    [GeneratedRegex(@"\A--https=(?<port>[1-9][0-9]{0,4})\z", RegexOptions.CultureInvariant)]
    private static partial Regex HttpsOption();

    [GeneratedRegex(@"\A--set-path=(?<path>/[a-z0-9][a-z0-9-]{0,31})\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixOption();

    /// <summary>
    /// The whole of rules TS-2 and TS-3 as a check of one argument list: it is one of the two reads or
    /// one of the four writes, with every value in its range, and it holds none of the forbidden words.
    /// </summary>
    private static void AssertIsOneOfTheSixArgumentLists(IReadOnlyList<string> arguments)
    {
        var text = string.Join(' ', arguments);
        Assert.DoesNotContain("reset", arguments);
        Assert.DoesNotContain("--yes", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("--tcp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(arguments, argument => argument.Contains("--tls-terminated-tcp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(arguments, argument => argument.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)));
        Assert.True(!arguments.Contains("off") || arguments.Any(argument => argument.StartsWith("--set-path=", StringComparison.Ordinal)), text);
        Assert.True(!arguments.Contains("--bg") || arguments.Any(argument => argument.StartsWith("--https=", StringComparison.Ordinal)), text);

        if (arguments.SequenceEqual(StatusRead) || arguments.SequenceEqual(ServeStatusRead))
        {
            return;
        }

        int Port(Regex pattern, string argument)
        {
            var match = pattern.Match(argument);
            Assert.True(match.Success, text);
            return int.Parse(match.Groups["port"].Value, CultureInfo.InvariantCulture);
        }

        void RelayPort(string argument)
        {
            var port = Port(RelayTarget(), argument);
            Assert.True(port is >= 1024 and <= 65535 && port != 47831, text);
        }

        switch (arguments.Count, arguments[0], arguments[^1])
        {
            case (4, "serve", "off"):
                Assert.InRange(Port(HttpsOption(), arguments[1]), 1, 65535);
                Assert.Equal("--set-path=/", arguments[2]);
                break;
            case (4, "funnel", "off"):
                Assert.Contains(Port(HttpsOption(), arguments[1]), FunnelPorts);
                Assert.Matches(PrefixOption(), arguments[2]);
                break;
            case (4, "serve", _):
                Assert.Equal("--bg", arguments[1]);
                var privatePort = Port(HttpsOption(), arguments[2]);
                Assert.InRange(privatePort, 1, 65535);
                Assert.DoesNotContain(privatePort, FunnelPorts);
                RelayPort(arguments[3]);
                break;
            case (5, "funnel", _):
                Assert.Equal("--bg", arguments[1]);
                Assert.Contains(Port(HttpsOption(), arguments[2]), FunnelPorts);
                Assert.Matches(PrefixOption(), arguments[3]);
                RelayPort(arguments[4]);
                break;
            default:
                Assert.Fail("Not one of the four command forms: tailscale " + text);
                break;
        }
    }

    /// <summary>What the confirmed plan may run, worked out here from the saved settings and the ledger alone.</summary>
    private static string[] Expected(MappingPlan plan, RelaySettings saved)
    {
        string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        switch (plan.Action)
        {
            case MappingAction.ApplyPrivate:
                return ["serve", "--bg", "--https=" + Number(saved.PrivateHttpsPort), "http://127.0.0.1:" + Number(saved.RelayPort)];
            case MappingAction.ApplyPublic:
                return ["funnel", "--bg", "--https=" + Number(saved.PublicHttpsPort), "--set-path=" + saved.PublicPathPrefix, "http://127.0.0.1:" + Number(saved.RelayPort)];
            default:
                // A removal ran: its entry is in the ledger, with exactly this mode, port and path.
                var entry = Assert.Single(
                    saved.OwnedMappings.Where(listed => listed is not null).Select(listed => (listed.Mode, listed.HttpsPort, listed.Path, listed.Target)).Distinct(),
                    listed => listed == (plan.Entry!.Mode, plan.Entry.HttpsPort, plan.Entry.Path, plan.Entry.Target));
                Assert.Equal(plan.Action == MappingAction.RemovePublic ? ConnectionMode.Public : ConnectionMode.Private, entry.Mode);
                return entry.Mode == ConnectionMode.Public
                    ? ["funnel", "--https=" + Number(entry.HttpsPort), "--set-path=" + entry.Path, "off"]
                    : ["serve", "--https=" + Number(entry.HttpsPort), "--set-path=/", "off"];
        }
    }

    private static string Describe(RelaySettings settings) =>
        $"relay {settings.RelayPort}, private {settings.PrivateHttpsPort}, public {settings.PublicHttpsPort}, prefix [{settings.PublicPathPrefix}]";

    /// <summary>Settings, ledgers, status documents and plans, good and hostile, from one seeded generator.</summary>
    private static class Generated
    {
        private static readonly int[] RelayPorts = [47841, 47841, 47841, 47851, 1024, 65535, 47831, 1023, 80, 0, -1, 65536, 47842];
        private static readonly int[] PrivatePorts = [11443, 11443, 11443, 12443, 1, 65535, 443, 8443, 10000, 0, -7, 65536];
        private static readonly int[] PublicPorts = [10000, 10000, 443, 8443, 11443, 444, 0, 65536];
        private static readonly int[] AnyPorts = [11443, 12443, 10000, 443, 8443, 444, 5432, 1, 65535];
        private static readonly int[] TargetPorts = [47841, 47841, 47851, 47831, 3000, 1024, 65535];
        private static readonly string[] Prefixes =
        [
            "/livecue", "/livecue", "/livecue", "/cue", "/a", "/x-1", "/", string.Empty, "/Live", "/a b", "/x --yes", "--tcp=443", "/reset",
            "/off", "/livecue\n", "/../..", "/-x", "/a/b", "/" + "abcdefghijklmnopqrstuvwxyz0123456789",
        ];

        private static readonly string[] Mounts =
        [
            "/", "/livecue", "/livecue/", "/cue", "/a", "/x-1", "/other", "/files", "/reset", "/off", "/ --yes", "/x off", "--tcp=443", "/a b", "/é",
        ];

        private static readonly string[] Targets =
        [
            "http://127.0.0.1:47841", "http://127.0.0.1:47851", "http://127.0.0.1:47831", "http://127.0.0.1:3000", "http://localhost:47841",
            "http://127.0.0.1:47841/", "https+insecure://127.0.0.1:47841", "reset", "--yes", "http://127.0.0.1:47841 off", "http://192.0.2.1:47841",
        ];

        public static T Pick<T>(Random random, params T[] values) => values[random.Next(values.Length)];

        /// <summary>Two times out of three settings a file could hold; otherwise each value from a list with hostile ones.</summary>
        public static RelaySettings Settings(Random random)
        {
            var settings = random.Next(3) == 0
                ? new RelaySettings
                {
                    RelayPort = Pick(random, RelayPorts),
                    ConnectionMode = Pick(random, ConnectionMode.Private, ConnectionMode.Public, (ConnectionMode)3),
                    PrivateHttpsPort = Pick(random, PrivatePorts),
                    PublicHttpsPort = Pick(random, PublicPorts),
                    PublicPathPrefix = Pick(random, Prefixes),
                }
                : new RelaySettings
                {
                    RelayPort = Pick(random, 47841, 47841, 47851, 1024, 65535),
                    ConnectionMode = Pick(random, ConnectionMode.Private, ConnectionMode.Public),
                    PrivateHttpsPort = Pick(random, 11443, 11443, 12443, 1, 65535),
                    PublicHttpsPort = Pick(random, 10000, 10000, 443, 8443),
                    PublicPathPrefix = Pick(random, "/livecue", "/livecue", "/cue", "/a", "/x-1"),
                };
            return settings with { OwnedMappings = [.. Enumerable.Range(0, random.Next(5)).Select(_ => Entry(random, settings))] };
        }

        /// <summary>A ledger entry: mostly the mapping of these settings, sometimes anything at all.</summary>
        public static OwnedMapping Entry(Random random, RelaySettings settings)
        {
            var isPublic = random.Next(2) == 0;
            var entry = isPublic
                ? new OwnedMapping(ConnectionMode.Public, settings.PublicHttpsPort, settings.PublicPathPrefix, "http://127.0.0.1:" + settings.RelayPort.ToString(CultureInfo.InvariantCulture), DateTimeOffset.UnixEpoch)
                : new OwnedMapping(ConnectionMode.Private, settings.PrivateHttpsPort, "/", "http://127.0.0.1:" + settings.RelayPort.ToString(CultureInfo.InvariantCulture), DateTimeOffset.UnixEpoch);
            return random.Next(4) switch
            {
                0 => entry with { HttpsPort = Pick(random, AnyPorts.Concat(PrivatePorts).ToArray()) },
                1 => entry with { Path = Pick(random, Mounts.Concat(Prefixes).ToArray()), Target = Pick(random, Targets), Mode = Pick(random, ConnectionMode.Private, ConnectionMode.Public, (ConnectionMode)9) },
                _ => entry,
            };
        }

        /// <summary>A serve status: often what the settings and the ledger expect, often something else, sometimes broken.</summary>
        public static string Document(Random random, RelaySettings settings)
        {
            switch (random.Next(12))
            {
                case 0:
                    return TailscaleFixtures.Malformed[random.Next(TailscaleFixtures.Malformed.Count)];
                case 1:
                    return TailscaleFixtures.NoHandlers;
                case 2:
                    return Pick(random, TailscaleFixtures.TailnetOnly443, TailscaleFixtures.FunnelRootAndMount443, TailscaleFixtures.SharedPortsAndTheRelay);
            }

            var document = ServeDoc.New();
            void Add(ServeDoc to, int port, string mount, string target)
            {
                if (port is >= 1 and <= 65535)
                {
                    to.Handler(port, mount, Pick(random, "Proxy", "Proxy", "Proxy", "Proxy", "Text", "Path"), target);
                }
            }

            // The handlers of the ledger, more often than not.
            foreach (var entry in settings.OwnedMappings)
            {
                if (random.Next(4) != 0)
                {
                    Add(document, entry.HttpsPort, entry.Path ?? "/", random.Next(8) == 0 ? Pick(random, Targets) : entry.Target ?? string.Empty);
                }
            }

            // The handlers the settings would make.
            if (random.Next(3) == 0)
            {
                Add(document, settings.PrivateHttpsPort, "/", "http://127.0.0.1:" + settings.RelayPort.ToString(CultureInfo.InvariantCulture));
            }

            if (random.Next(3) == 0)
            {
                Add(document, settings.PublicHttpsPort, settings.PublicPathPrefix, "http://127.0.0.1:" + settings.RelayPort.ToString(CultureInfo.InvariantCulture));
            }

            // Neighbours, Funnel, raw TCP, a foreground session, a service.
            for (var extra = random.Next(4); extra > 0; extra--)
            {
                Add(document, Pick(random, AnyPorts), Pick(random, Mounts), Pick(random, Targets));
            }

            foreach (var port in AnyPorts.Append(settings.PublicHttpsPort).Append(settings.PrivateHttpsPort).Where(port => port is >= 1 and <= 65535))
            {
                if (random.Next(4) == 0)
                {
                    document.Funnel(port, on: random.Next(5) != 0);
                }
            }

            if (random.Next(10) == 0)
            {
                document.TcpForward(Pick(random, AnyPorts));
            }

            if (random.Next(10) == 0)
            {
                var session = ServeDoc.New();
                Add(session, Pick(random, AnyPorts), Pick(random, Mounts), Pick(random, Targets));
                document.Foreground("session-1", session.Funnel(Pick(random, AnyPorts)));
            }

            if (random.Next(15) == 0)
            {
                var service = ServeDoc.New();
                Add(service, Pick(random, AnyPorts), Pick(random, Mounts), Pick(random, Targets));
                document.Service("svc:example", service);
            }

            return document.Json();
        }

        /// <summary>
        /// A plan to confirm, or null for "inspect instead". Mostly the plans the manager itself makes;
        /// sometimes one put together from parts, one with an altered argument list, or one that is stale.
        /// </summary>
        public static MappingPlan? Plan(Random random, MappingHarness harness, RelaySettings saved)
        {
            var manager = harness.Manager;
            MappingPlan? Removal(OwnedMapping? entry)
            {
                try
                {
                    return entry is null ? null : manager.PlanRemoval(entry, Pick<string?>(random, null, "pc.example.test", "pc.example.test --yes"));
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            var ledger = saved.OwnedMappings;
            var fromLedger = Removal(ledger.Count == 0 ? null : ledger[random.Next(ledger.Count)]);
            var apply = manager.PlanApply(Pick(random, ConnectionMode.Private, ConnectionMode.Public), saved, Pick<string?>(random, null, "pc.example.test", "reset"), out _);

            switch (random.Next(14))
            {
                case 0:
                    return null;
                case 1 or 2 or 3 or 4:
                    return apply;
                case 5 or 6 or 7 or 8:
                    return fromLedger;
                case 9:
                    // An entry that is not in the ledger, or the plan of other settings.
                    return Pick(random, Removal(Entry(random, Settings(random))), manager.PlanApply(ConnectionMode.Private, Settings(random), null, out _));
                case 10:
                    // The action and entry of one plan with the command of another.
                    return apply is null || fromLedger is null
                        ? null
                        : Pick(random, apply with { Command = fromLedger.Command }, fromLedger with { Command = apply.Command }, apply with { Entry = fromLedger.Entry }, fromLedger with { Action = apply.Action });
                case 11:
                    // An argument list changed after the factory returned.
                    var plan = Pick(random, apply, fromLedger);
                    if (plan is not null)
                    {
                        var arguments = (string[])plan.Command.Arguments;
                        arguments[random.Next(arguments.Length)] = Pick(random, "reset", "--yes", "--tcp=443", "off", "--https=443", "--set-path=/", "http://127.0.0.1:47831", "serve", "funnel");
                    }

                    return plan;
                case 12:
                    // A well-formed command for a port and path that are not the configured ones.
                    return apply is null
                        ? null
                        : apply with { Command = Pick(random, MappingCommand.ApplyPrivate(12001, 47841), MappingCommand.ApplyPublic(443, "/other", 47841), MappingCommand.RemovePrivate(443), MappingCommand.RemovePublic(8443, "/files")) };
                default:
                    return fromLedger is null
                        ? apply
                        : fromLedger with { Command = Pick(random, MappingCommand.RemovePrivate(443), MappingCommand.RemovePublic(443, "/files"), MappingCommand.RemovePublic(10000, "/other")) };
            }
        }
    }
}
