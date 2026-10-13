using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// The texts of W-design 4.15, word for word: what a confirmation dialog says a command will do, why
/// the manager ran nothing, how a command ended, and what an exposure warning reads.
/// </summary>
public sealed class MappingTextsTests
{
    private const string StatusNotRead =
        "LiveCue could not read Tailscale's configuration, so it changed nothing. Check that Tailscale is running, then choose Check again.";

    [Fact]
    public void Every_refusal_and_outcome_has_its_text()
    {
        // ---- Refusals: one text for every member of the enumeration. ----
        var refusals = new Dictionary<MappingRefusal, string>
        {
            [MappingRefusal.None] = string.Empty,
            [MappingRefusal.TailscaleNotInstalled] = "Tailscale is not installed in Program Files.",
            [MappingRefusal.StatusUnavailable] = StatusNotRead,
            [MappingRefusal.StatusUnreadable] = StatusNotRead,
            [MappingRefusal.InvalidSettings] = "The connection settings are not valid. Check the ports and the path, then save.",
            [MappingRefusal.ForbiddenRelayPort] = "Port 47831 belongs to the old LiveCue and is never used. Choose another relay port.",
            [MappingRefusal.PrivatePortOccupied] = "Port 8443 already has other Tailscale handlers. Choose another private HTTPS port.",
            [MappingRefusal.PrivatePortHasFunnel] =
                "Port 8443 has Funnel turned on. A private listener there would turn Funnel off for the whole port. Choose another private HTTPS port.",
            [MappingRefusal.PublicMountOccupied] = "The path /cue on port 8443 is already used by something else. Choose another path.",
            [MappingRefusal.PublicPortHasTcpForward] = "Port 8443 forwards raw TCP and cannot take a Funnel path. Choose another public port.",
            [MappingRefusal.PublicPortWouldPublishOthers] =
                "Port 8443 has tailnet-only handlers. Turning Funnel on there would publish them to the internet. Choose a port that already has Funnel on, or an unused one.",
            [MappingRefusal.AlreadyPresent] = "The connection is already set up.",
            [MappingRefusal.NotPresent] = "That mapping no longer exists in Tailscale. LiveCue removed it from its own list.",
            [MappingRefusal.NotInLedger] = "This mapping was not created by LiveCue, so LiveCue will not remove it.",
            [MappingRefusal.LedgerMismatch] = "The mapping no longer matches what LiveCue created, so LiveCue will not remove it.",
            [MappingRefusal.PlanOutOfDate] = "The settings changed while this dialog was open. Close it and look at the command again.",
        };

        Assert.Equal(Enum.GetValues<MappingRefusal>().Order(), refusals.Keys.Order());
        Assert.All(refusals, pair => Assert.Equal(pair.Value, MappingTexts.Refusal(pair.Key, 8443, "/cue")));
        Assert.Throws<ArgumentOutOfRangeException>(() => MappingTexts.Refusal((MappingRefusal)999, 8443, "/cue"));

        // ---- Outcomes: one text for every member, for an apply and for a removal. ----
        const string TimedOut =
            "Tailscale did not finish in time. Run the command shown above in a terminal to see Tailscale's own instructions. The first use of HTTPS or Funnel on a tailnet must be approved in the Tailscale admin console.";
        const string NotSaved =
            "The mapping changed, but LiveCue could not save its settings. LiveCue will treat the mapping as not its own.";
        var outcomes = new Dictionary<MappingOutcome, (string Apply, string Remove)>
        {
            [MappingOutcome.Done] = (string.Empty, string.Empty),
            [MappingOutcome.Refused] = (string.Empty, string.Empty),
            [MappingOutcome.CommandFailed] = (
                "Tailscale HTTPS setup failed. Check that Tailscale is connected.",
                "Tailscale could not remove the mapping. Check that Tailscale is connected."),
            [MappingOutcome.CommandTimedOut] = (TimedOut, TimedOut),
            [MappingOutcome.NotVerified] = (
                "Tailscale reported success, but the mapping is not in its configuration. Choose Check again.",
                "Tailscale reported success, but LiveCue could not confirm that the mapping is gone. Choose Check again."),
            [MappingOutcome.LedgerNotSaved] = (NotSaved, NotSaved),
        };

        Assert.Equal(Enum.GetValues<MappingOutcome>().Order(), outcomes.Keys.Order());
        Assert.All(outcomes, pair =>
        {
            Assert.Equal(pair.Value.Apply, MappingTexts.Outcome(pair.Key, MappingAction.ApplyPrivate));
            Assert.Equal(pair.Value.Apply, MappingTexts.Outcome(pair.Key, MappingAction.ApplyPublic));
            Assert.Equal(pair.Value.Remove, MappingTexts.Outcome(pair.Key, MappingAction.RemovePrivate));
            Assert.Equal(pair.Value.Remove, MappingTexts.Outcome(pair.Key, MappingAction.RemovePublic));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => MappingTexts.Outcome((MappingOutcome)999, MappingAction.ApplyPrivate));

        // What a dialog shows for a result: the refusal's text with the plan's port and path, else the outcome's.
        var harness = new MappingHarness(new RelaySettings { ConnectionMode = ConnectionMode.Public });
        var apply = harness.PlanApply(ConnectionMode.Public);
        var removal = harness.Manager.PlanRemoval(TailscaleFixtures.PublicEntry(8443, "/cue"), null);
        MappingResult Result(MappingOutcome outcome, MappingRefusal refusal = MappingRefusal.None) =>
            new(outcome, refusal, OthersChanged: false, harness.Settings, After: null);

        Assert.Equal(string.Empty, MappingTexts.Failure(Result(MappingOutcome.Done), apply));
        Assert.Equal(
            "The path /livecue on port 10000 is already used by something else. Choose another path.",
            MappingTexts.Failure(Result(MappingOutcome.Refused, MappingRefusal.PublicMountOccupied), apply));
        Assert.Equal(outcomes[MappingOutcome.CommandFailed].Apply, MappingTexts.Failure(Result(MappingOutcome.CommandFailed), apply));
        Assert.Equal(outcomes[MappingOutcome.CommandFailed].Remove, MappingTexts.Failure(Result(MappingOutcome.CommandFailed), removal));
        Assert.Equal(outcomes[MappingOutcome.NotVerified].Remove, MappingTexts.Failure(Result(MappingOutcome.NotVerified), removal));
        Assert.Equal(refusals[MappingRefusal.LedgerMismatch], MappingTexts.Failure(Result(MappingOutcome.Refused, MappingRefusal.LedgerMismatch), removal));

        // ---- Effects: the four sentences, with the address and without a name. ----
        Assert.Equal(
            "Tailscale serves https://pc.example.test:11443/ inside your tailnet and forwards it to the LiveCue relay on this PC (port 47841, this PC only). Nothing becomes reachable from the internet.",
            MappingTexts.Effect(MappingAction.ApplyPrivate, "https://pc.example.test:11443/", 47841));
        Assert.Equal(
            "Tailscale Funnel publishes https://pc.example.test:10000/livecue/ on the internet and forwards it to the LiveCue relay on this PC (port 47841, this PC only). Anyone can reach the address; only an iPhone with the pairing token gets past it.",
            MappingTexts.Effect(MappingAction.ApplyPublic, "https://pc.example.test:10000/livecue/", 47841));
        Assert.Equal(
            "Tailscale stops serving https://pc.example.test:11443/ inside your tailnet. iPhones paired with that address can no longer reach this PC.",
            MappingTexts.Effect(MappingAction.RemovePrivate, "https://pc.example.test:11443/", 0));
        Assert.Equal(
            "Tailscale stops publishing https://pc.example.test:10000/livecue/ on the internet. iPhones paired with that address can no longer reach this PC.",
            MappingTexts.Effect(MappingAction.RemovePublic, "https://pc.example.test:10000/livecue/", 0));
        Assert.Equal(
            "Tailscale serves this PC's Tailscale address inside your tailnet and forwards it to the LiveCue relay on this PC (port 47851, this PC only). Nothing becomes reachable from the internet.",
            MappingTexts.Effect(MappingAction.ApplyPrivate, null, 47851));
        Assert.Equal(
            "Tailscale stops publishing this PC's Tailscale address on the internet. iPhones paired with that address can no longer reach this PC.",
            MappingTexts.Effect(MappingAction.RemovePublic, null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MappingTexts.Effect((MappingAction)999, null, 47841));

        // The plans carry exactly these sentences, with the advertised endpoint of their own mapping.
        var settings = new RelaySettings { RelayPort = 47851, PublicHttpsPort = 8443, PublicPathPrefix = "/cue" };
        var manager = harness.Manager;
        Assert.Equal(
            MappingTexts.Effect(MappingAction.ApplyPrivate, "https://pc.example.test:11443/", 47851),
            manager.PlanApply(ConnectionMode.Private, settings, "PC.Example.Test.", out _)!.EffectText);
        Assert.Equal(
            MappingTexts.Effect(MappingAction.ApplyPublic, "https://pc.example.test:8443/cue/", 47851),
            manager.PlanApply(ConnectionMode.Public, settings, ServeStatusFixtures.DnsName, out _)!.EffectText);
        Assert.Equal(
            MappingTexts.Effect(MappingAction.RemovePrivate, "https://pc.example.test:12443/", 0),
            manager.PlanRemoval(TailscaleFixtures.PrivateEntry(12443), ServeStatusFixtures.DnsName).EffectText);
        Assert.Equal(
            MappingTexts.Effect(MappingAction.RemovePublic, "https://pc.example.test:8443/cue/", 0),
            manager.PlanRemoval(TailscaleFixtures.PublicEntry(8443, "/cue"), ServeStatusFixtures.DnsName).EffectText);

        // A name that is unknown, or that is no host name at all, is never put into the sentence.
        foreach (var name in (string?[])[null, string.Empty, "  ", "pc.example.test --yes", "pc.example.test/x", "pc\n.example.test"])
        {
            Assert.Equal(
                MappingTexts.Effect(MappingAction.ApplyPublic, null, 47851),
                manager.PlanApply(ConnectionMode.Public, settings, name, out _)!.EffectText);
        }

        // ---- The standing texts. ----
        Assert.Equal(
            "Warning: other entries of Tailscale's serve configuration changed during this command. LiveCue repaired nothing. Review them with: tailscale serve status",
            MappingTexts.OthersChanged);
        Assert.Equal(
            "Tailscale's configuration could not be read, so LiveCue cannot tell what is exposed. Choose Check again.",
            MappingTexts.ExposureUnknown);

        // ---- Exposure warnings. ----
        const string Published =
            "A Tailscale Funnel mapping on port 8443, path /by-hand, publishes this relay on the internet. LiveCue did not create it and will not remove it.";
        const string TailnetOnly =
            "A Tailscale mapping on port 11443, path /, forwards to this relay inside your tailnet. LiveCue did not create it and will not remove it.";
        const string Session = " It belongs to a running Tailscale session or service; end that to remove it.";

        Assert.Equal(Published, MappingTexts.Exposure(new ExposedHandler(8443, "/by-hand", FunnelOn: true, Foreground: false, LedgerEntry: null)));
        Assert.Equal(TailnetOnly, MappingTexts.Exposure(new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, LedgerEntry: null)));
        Assert.Equal(Published + Session, MappingTexts.Exposure(new ExposedHandler(8443, "/by-hand", FunnelOn: true, Foreground: true, LedgerEntry: null)));
        Assert.Equal(TailnetOnly + Session, MappingTexts.Exposure(new ExposedHandler(11443, "/", FunnelOn: false, Foreground: true, LedgerEntry: null)));

        // The mount is Tailscale's text, not ours: it is shown on one line and at a bounded length.
        var hostile = MappingTexts.Exposure(new ExposedHandler(443, "/a\r\nLiveCue says: run this\u202E\0", FunnelOn: true, Foreground: false, LedgerEntry: null));
        Assert.DoesNotContain(hostile, character => char.IsControl(character) || character == '\u202E');
        Assert.StartsWith("A Tailscale Funnel mapping on port 443, path /a", hostile, StringComparison.Ordinal);
        var longMount = MappingTexts.Exposure(new ExposedHandler(443, "/" + new string('a', 5000), FunnelOn: false, Foreground: false, LedgerEntry: null));
        Assert.InRange(longMount.Length, 1, 300);
    }

    [Fact]
    public void Manual_removal_cases()
    {
        static ExposedHandler Row(int port, string mount, bool funnel, bool foreground = false) => new(port, mount, funnel, foreground, LedgerEntry: null);

        // Both forms, each naming its port and its path.
        Assert.Equal("tailscale funnel --https=8443 --set-path=/by-hand off", MappingTexts.ManualRemoval(Row(8443, "/by-hand", funnel: true)));
        Assert.Equal("tailscale serve --https=11443 --set-path=/ off", MappingTexts.ManualRemoval(Row(11443, "/", funnel: false)));
        Assert.Equal("tailscale funnel --https=443 --set-path=/ off", MappingTexts.ManualRemoval(Row(443, "/", funnel: true)));
        Assert.Equal(
            "tailscale serve --https=12443 --set-path=/Files/v1.2/_x~y-z/ off",
            MappingTexts.ManualRemoval(Row(12443, "/Files/v1.2/_x~y-z/", funnel: false)));

        // Empty for a foreground session or a service: ending that is what removes it.
        Assert.Equal(string.Empty, MappingTexts.ManualRemoval(Row(8443, "/by-hand", funnel: true, foreground: true)));
        Assert.Equal(string.Empty, MappingTexts.ManualRemoval(Row(11443, "/", funnel: false, foreground: true)));

        // Empty for a mount with a character outside the safe set: it is never put into a command
        // somebody is asked to paste into a terminal.
        string[] unsafeMounts =
        [
            "/a b", "/a off", "/ --yes", "/x;reset", "/x&reset", "/x|reset", "/x`reset`", "/$(reset)", "/%PATH%", "/\"x\"", "/'x'", "/x\\y",
            "/x\ny", "/x\r\n", "/x\ty", "/x\0", "/é", "/x\u202Ey", "/x=y", "/x,y", "/x:y", "/x?y", "/x#y", "/x*", "/<x>", "/x!", "/x^y", "/(x)",
            "livecue", "--set-path=/", "off", " /x", "/x ", string.Empty,
        ];
        Assert.All(unsafeMounts, mount =>
        {
            Assert.Equal(string.Empty, MappingTexts.ManualRemoval(Row(8443, mount, funnel: true)));
            Assert.Equal(string.Empty, MappingTexts.ManualRemoval(Row(8443, mount, funnel: false)));
        });
        Assert.Equal(string.Empty, MappingTexts.ManualRemoval(Row(8443, null!, funnel: true)));

        // "/" and up to 128 further characters, and no more.
        var longest = "/" + new string('a', 128);
        Assert.Equal($"tailscale serve --https=8443 --set-path={longest} off", MappingTexts.ManualRemoval(Row(8443, longest, funnel: false)));
        Assert.Equal(string.Empty, MappingTexts.ManualRemoval(Row(8443, longest + "a", funnel: false)));

        // A port that is none.
        Assert.All(new[] { int.MinValue, -1, 0, 65536, int.MaxValue }, port =>
            Assert.Equal(string.Empty, MappingTexts.ManualRemoval(Row(port, "/x", funnel: true))));

        // What the Settings row reads when there is no command to show.
        Assert.Equal("Remove it with Tailscale's own tools: tailscale serve status", MappingTexts.ManualRemovalUnavailable);

        // Every command that is shown is one line of plain characters and one of the two remove forms.
        // It is text: nothing in the application takes it, or the row it was made from, and runs it.
        ExposedHandler[] shown = [Row(8443, "/by-hand", funnel: true), Row(11443, "/", funnel: false), Row(1, "/a/b/c", funnel: false), Row(65535, "/-", funnel: true)];
        Assert.All(shown, row =>
        {
            var command = MappingTexts.ManualRemoval(row);
            var words = command.Split(' ');
            Assert.Equal(5, words.Length);
            Assert.Equal("tailscale", words[0]);
            Assert.Equal(row.FunnelOn ? "funnel" : "serve", words[1]);
            Assert.Equal("--https=" + row.HttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture), words[2]);
            Assert.Equal("--set-path=" + row.Mount, words[3]);
            Assert.Equal("off", words[4]);
            Assert.DoesNotContain("reset", words);
            Assert.DoesNotContain("--yes", words);
            Assert.DoesNotContain("--bg", words);
        });
        Assert.Equal(typeof(string), typeof(MappingTexts).GetMethod(nameof(MappingTexts.ManualRemoval))!.ReturnType);
        Assert.DoesNotContain(
            typeof(MappingTexts).GetMethods().Select(method => method.ReturnType),
            type => type == typeof(MappingCommand) || type == typeof(MappingPlan));
    }
}
