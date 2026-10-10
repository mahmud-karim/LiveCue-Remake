using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Prerequisites;

/// <summary>
/// The status-line texts, character for character as architecture 3.11 and 3.7 give them (test plan 2.3).
/// The expected texts are written out here on purpose: a changed word must fail a test.
/// </summary>
public sealed class PrerequisiteTextsTests
{
    [Fact]
    public void Texts_match_the_architecture()
    {
        Assert.Equal("LiveCue could not open its data folder.", PrerequisiteTexts.Failure(PrerequisiteId.DataFolder));
        Assert.Equal(
            "Codex CLI was not found. Install or update the Codex desktop app, then open it once and sign in.",
            PrerequisiteTexts.Failure(PrerequisiteId.CodexFound));
        Assert.Equal(
            "Codex is installed but not signed in. Open Codex and sign in with ChatGPT.",
            PrerequisiteTexts.Failure(PrerequisiteId.CodexSignedIn));
        Assert.Equal("Tailscale is not installed in Program Files.", PrerequisiteTexts.Failure(PrerequisiteId.TailscaleInstalled));
        Assert.Equal("Tailscale is not online. Open Tailscale and connect first.", PrerequisiteTexts.Failure(PrerequisiteId.TailscaleOnline));
        Assert.Equal("Tailscale did not return this PC DNS name.", PrerequisiteTexts.Failure(PrerequisiteId.TailscaleDnsName));

        // Every check has a text, and no other value has one.
        Assert.All(Enum.GetValues<PrerequisiteId>(), id => Assert.NotEmpty(PrerequisiteTexts.Failure(id)));
        Assert.Equal(6, Enum.GetValues<PrerequisiteId>().Select(PrerequisiteTexts.Failure).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => PrerequisiteTexts.Failure((PrerequisiteId)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrerequisiteTexts.Failure((PrerequisiteId)7));

        Assert.Equal("LiveCue Relay prerequisites passed.", PrerequisiteTexts.Passed);

        // Check 6a.
        Assert.Equal(
            "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode.",
            PrerequisiteTexts.ExposedInPrivateMode);
    }

    [Fact]
    public void Start_fault_texts_match_the_architecture()
    {
        const string plain =
            "Port 47841 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.";
        const string withMapping =
            "Port 47841 is in use by another program while a Tailscale mapping points at it. That program may have received your iPhone's requests, including the pairing token. Free the port, start the relay, then reset pairing.";
        const string pairing = "The saved pairing could not be read. Reset pairing to create a new one.";
        const string other = "Could not start the relay.";

        Assert.Equal(plain, PrerequisiteTexts.StartFault(RelayFaultCode.PortInUse, 47841));
        Assert.Equal(plain, PrerequisiteTexts.StartFault(RelayFaultCode.PortInUse, 47841, mappingPointsAtPort: false));
        Assert.Equal(withMapping, PrerequisiteTexts.StartFault(RelayFaultCode.PortInUse, 47841, mappingPointsAtPort: true));

        // The flag changes the port-in-use text only.
        foreach (var flag in (bool[])[false, true])
        {
            Assert.Equal(pairing, PrerequisiteTexts.StartFault(RelayFaultCode.PairingStoreUnreadable, 47841, flag));
            Assert.Equal(other, PrerequisiteTexts.StartFault(RelayFaultCode.InvalidEndpoint, 47841, flag));
            Assert.Equal(other, PrerequisiteTexts.StartFault(RelayFaultCode.StartFailed, 47841, flag));
            Assert.Equal(other, PrerequisiteTexts.StartFault(RelayFaultCode.Internal, 47841, flag));
        }

        Assert.Equal(pairing, PrerequisiteTexts.StartFault(RelayFaultCode.PairingStoreUnreadable, 47841));
        Assert.Equal(other, PrerequisiteTexts.StartFault(RelayFaultCode.StartFailed, 47841));

        // The port is written as plain digits whatever the culture of the thread.
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.StartsWith("Port 50123 is already in use.", PrerequisiteTexts.StartFault(RelayFaultCode.PortInUse, 50123), StringComparison.Ordinal);
            Assert.StartsWith("Port 50123 is in use by another program", PrerequisiteTexts.StartFault(RelayFaultCode.PortInUse, 50123, true), StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [Fact]
    public void The_containment_warning_has_its_four_forms()
    {
        const string advice = " Run the containment check, then choose Mark as checked in Settings.";

        Assert.Equal(
            "Codex is version codex-cli 1.2.3; the containment check was last run for codex-cli 1.2.2." + advice,
            PrerequisiteTexts.ContainmentWarning("codex-cli 1.2.3", "codex-cli 1.2.2"));
        Assert.Equal(
            "Codex is version codex-cli 1.2.3; the containment check has not been run yet." + advice,
            PrerequisiteTexts.ContainmentWarning("codex-cli 1.2.3", null));
        Assert.Equal(
            "Codex's version could not be read; the containment check was last run for codex-cli 1.2.2." + advice,
            PrerequisiteTexts.ContainmentWarning(null, "codex-cli 1.2.2"));
        Assert.Equal(
            "Codex's version could not be read; the containment check has not been run yet." + advice,
            PrerequisiteTexts.ContainmentWarning(null, null));
    }
}
