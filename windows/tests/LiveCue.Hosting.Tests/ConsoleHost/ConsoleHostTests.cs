using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveCue.Hosting.Tests.Prerequisites;
using LiveCue.Hosting.Tests.Supervisor;
using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.Relay.Host;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Hosting.Tests.ConsoleHost;

/// <summary>
/// The console host of architecture 3.14, called without its process (test plan 2.4, and ST-15 and ST-16
/// of section 4.6). The hosting library is the real one over scripted checks, scripted mappings and fake
/// relays, except where a test says that the relay is real.
/// </summary>
public sealed partial class ConsoleHostTests
{
    private const string Endpoint = "https://pc.example.test:11443/";
    private const string Canary = "CANARY-7f3a-conversation-text";
    private const string StaleWarning =
        "Codex is version codex-cli 1.2.3; the containment check was last run for codex-cli 1.2.2. Run the containment check, then choose Mark as checked in Settings.";

    private static readonly string[] RunWithEndpoint = ["--endpoint", Endpoint, "--port", "50123"];

    [Fact]
    public async Task Check_only()
    {
        var passed = new HostRun();
        Assert.Equal(0, await passed.RunAsync("--check-only"));
        Assert.Equal(["LiveCue Relay prerequisites passed."], passed.Lines);

        // Each failed check: its text of architecture 3.11, and exit code 2.
        foreach (var id in Enum.GetValues<PrerequisiteId>())
        {
            var failed = new HostRun();
            failed.Prerequisites.Fails = id;
            Assert.Equal(2, await failed.RunAsync("--check-only"));
            Assert.Equal([PrerequisiteTexts.Failure(id)], failed.Lines);
            Assert.Empty(failed.Relays.Created);
        }

        // With an endpoint the Tailscale checks are skipped, so a PC without Tailscale passes.
        var withEndpoint = new HostRun();
        withEndpoint.Prerequisites.Fails = PrerequisiteId.TailscaleInstalled;
        Assert.Equal(0, await withEndpoint.RunAsync("--check-only", "--endpoint", Endpoint));
        Assert.Equal(["LiveCue Relay prerequisites passed."], withEndpoint.Lines);

        // Nothing was started and nothing of Tailscale's configuration was read.
        Assert.Empty(passed.Relays.Created);
        Assert.Empty(passed.Mappings.Inspections);
        Assert.Single(passed.Prerequisites.Calls);
    }

    [Fact]
    public async Task A_start_fault_is_exit_3()
    {
        var run = new HostRun();
        run.Relays.NextStartFault = RelayFaultCode.PortInUse;

        Assert.Equal(3, await run.RunAsync(RunWithEndpoint));

        Assert.Equal(
            [
                "Checking Codex and Tailscale...",
                "Starting the relay on 127.0.0.1:50123.",
                "Relay fault: PORT_IN_USE.",
                "Port 50123 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.",
            ],
            run.Lines);
        Assert.True(run.Relays.Current.Disposed);

        // The other start faults, and a start that never reaches a relay.
        var unreadable = new HostRun();
        unreadable.Relays.NextStartFault = RelayFaultCode.PairingStoreUnreadable;
        Assert.Equal(3, await unreadable.RunAsync(RunWithEndpoint));
        Assert.Equal("The saved pairing could not be read. Reset pairing to create a new one.", unreadable.Lines[^1]);

        var noEndpoint = new HostRun();
        noEndpoint.Prerequisites.Settings = new RelaySettings
        {
            ConnectionMode = ConnectionMode.Public,
            PublicPathPrefix = "/not a prefix",
            CodexContainmentCheckedVersion = ScriptedPrerequisites.Version,
        };
        Assert.Equal(3, await noEndpoint.RunAsync());
        Assert.Equal(["Checking Codex and Tailscale...", "Relay fault: INVALID_ENDPOINT.", "Could not start the relay."], noEndpoint.Lines);
        Assert.Empty(noEndpoint.Relays.Created);

        // A failed prerequisite is not a start fault: exit code 2 and its own text.
        var notSignedIn = new HostRun();
        notSignedIn.Prerequisites.Fails = PrerequisiteId.CodexSignedIn;
        Assert.Equal(2, await notSignedIn.RunAsync(RunWithEndpoint));
        Assert.Equal(
            ["Checking Codex and Tailscale...", "Codex is installed but not signed in. Open Codex and sign in with ChatGPT."],
            notSignedIn.Lines);
        Assert.Empty(notSignedIn.Relays.Created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_token_is_never_printed_by_default(bool json)
    {
        // The real relay with a real pairing, on a port the system picks, in a temporary data folder.
        // Only the Codex is not real: an empty file, and a process runner that starts nothing.
        using var temp = new TempFolder();
        var codex = temp.Combine("codex.exe");
        await File.WriteAllBytesAsync(codex, []);
        var relays = new CapturingRelayFactory(new ProductionRelayFactory(TimeProvider.System, new FakeProcessRunner()));
        using var output = new StringWriter();
        using var cancel = new CancellationTokenSource();
        string[] args =
        [
            "--endpoint", Endpoint, "--port", "0", "--data-dir", temp.Combine("data"), "--codex", codex,
            .. json ? (string[])["--events", "json", "--include-content"] : [],
        ];

        var running = HostProgram.RunAsync(
            args,
            arguments => HostingFactory.Create(
                new SupervisorOptions(arguments.DataDirectory!, "1.0.0", arguments.Overrides),
                new HostingOverrides { Processes = new FakeProcessRunner(), RelayFactory = relays }),
            new HostOutput(output, IsRedirected: true),
            cancel.Token);
        await WaitForAsync(() => relays.Created is { Status: RelayStatus.Running });
        var relay = relays.Created!;
        var port = relay.BoundPort;

        // A phone that knows the token asks for health, and a stranger asks without one.
        var payload = await ((IPairingExport)relay).GetPairingPayloadAsync(CancellationToken.None);
        var token = JsonDocument.Parse(payload).RootElement.GetProperty("token").GetString()!;
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/health", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        request.Headers.TryAddWithoutValidation("X-LiveCue-Protocol", "2");
        using var answered = await client.SendAsync(request);
        using var refused = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        await cancel.CancelAsync();
        Assert.Equal(0, await running.WaitAsync(TimeSpan.FromSeconds(30)));
        var text = output.ToString();

        // The run is all there, and the phone was seen.
        Assert.Contains(json ? "\"type\":\"RelayStarted\"" : "started on 127.0.0.1:" + port, text, StringComparison.Ordinal);
        Assert.Contains(json ? "\"type\":\"PhoneSeen\"" : "iPhone seen.", text, StringComparison.Ordinal);
        Assert.Contains(json ? "\"type\":\"RelayStopped\"" : "Relay stopped.", text, StringComparison.Ordinal);
        Assert.Contains(Endpoint, text, StringComparison.Ordinal);

        // No line holds the token, the payload, or anything of the token's shape.
        Assert.Matches(TokenShape(), token);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\"", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(TokenShape(), text);
        Assert.DoesNotContain(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_pairing_prints_the_payload_once()
    {
        using var data = new TempFolder();
        await PairingMaintenance.ResetStoredPairingAsync(data.Path, CancellationToken.None);
        var pairingFile = await File.ReadAllBytesAsync(data.Combine("pairing.json"));
        var run = new HostRun();

        Assert.Equal(0, await run.RunAsync("--show-pairing", "--endpoint", Endpoint, "--data-dir", data.Path));

        // The first line is the payload text of protocol 4.2, for the endpoint that was resolved.
        var payload = run.Lines[0];
        var match = PayloadShape().Match(payload);
        Assert.True(match.Success, "The first line is not a pairing payload.");
        Assert.Equal(Endpoint, match.Groups["endpoint"].Value);
        var token = match.Groups["token"].Value;

        // It is the stored pairing's token: its hash is the one in pairing.json.
        var tokenHash = JsonDocument.Parse(pairingFile).RootElement.GetProperty("tokenHash").GetString();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))), tokenHash);

        // Printed exactly once, and followed by the terminal QR of that payload and nothing else.
        Assert.Equal(1, Regex.Count(run.Output.ToString(), Regex.Escape(token)));
        Assert.Equal(TerminalQr.Lines(payload), run.Lines.Skip(1));
        Assert.All(run.Lines.Skip(1), line => Assert.Matches("^[\u2588\u2580\u2584 ]+$", line));

        // No relay was created, so no port was bound; and the pairing was only read.
        Assert.Empty(run.Relays.Created);
        Assert.Equal(pairingFile, await File.ReadAllBytesAsync(data.Combine("pairing.json")));
        Assert.Equal(["pairing.json"], data.FileNames());

        // What the checks say about Codex does not matter here, and Tailscale is not asked with an endpoint.
        var withoutCodex = new HostRun();
        withoutCodex.Prerequisites.Fails = PrerequisiteId.CodexFound;
        Assert.Equal(0, await withoutCodex.RunAsync("--show-pairing", "--endpoint", Endpoint, "--data-dir", data.Path));
        Assert.Equal(payload, withoutCodex.Lines[0]);
    }

    [Fact]
    public async Task Show_pairing_resolves_the_endpoint_the_way_a_start_does()
    {
        using var data = new TempFolder();
        await PairingMaintenance.ResetStoredPairingAsync(data.Path, CancellationToken.None);

        // No endpoint given: the mode, ports and prefix of the settings with this PC's Tailscale name.
        var privateMode = new HostRun();
        Assert.Equal(0, await privateMode.RunAsync("--show-pairing", "--data-dir", data.Path));
        Assert.StartsWith("{\"endpoint\":\"https://pc.example.test:11443/\",\"token\":\"", privateMode.Lines[0], StringComparison.Ordinal);

        var publicMode = new HostRun();
        publicMode.Prerequisites.Settings = new RelaySettings { PublicHttpsPort = 8443, PublicPathPrefix = "/cue" };
        Assert.Equal(0, await publicMode.RunAsync("--show-pairing", "--data-dir", data.Path, "--mode", "public"));
        Assert.StartsWith("{\"endpoint\":\"https://pc.example.test:8443/cue/\",\"token\":\"", publicMode.Lines[0], StringComparison.Ordinal);

        // It works whatever a mapping inspection would say: none is made, and nothing is started.
        Assert.Empty(publicMode.Mappings.Inspections);
        Assert.Empty(publicMode.Relays.Created);
    }

    [Fact]
    public async Task Show_pairing_says_why_it_cannot_show_one()
    {
        // No pairing has been created yet: the fixed text, exit code 2, and nothing is created.
        using var empty = new TempFolder();
        var none = new HostRun();
        Assert.Equal(2, await none.RunAsync("--show-pairing", "--endpoint", Endpoint, "--data-dir", empty.Path));
        Assert.Equal(["No pairing has been created yet. Start LiveCue once, then run this again."], none.Lines);
        Assert.Empty(none.Relays.Created);
        Assert.Empty(empty.FileNames());

        // A pairing file that cannot be read: its own fixed text, and the file is left as it is.
        using var broken = new TempFolder();
        await File.WriteAllTextAsync(broken.Combine("pairing.json"), "{ not a pairing }");
        var unreadable = new HostRun();
        Assert.Equal(2, await unreadable.RunAsync("--show-pairing", "--endpoint", Endpoint, "--data-dir", broken.Path));
        Assert.Equal(["The saved pairing could not be read. Reset pairing to create a new one."], unreadable.Lines);
        Assert.Equal("{ not a pairing }", await File.ReadAllTextAsync(broken.Combine("pairing.json")));

        // No endpoint: the check that stands in the way says so.
        using var data = new TempFolder();
        await PairingMaintenance.ResetStoredPairingAsync(data.Path, CancellationToken.None);
        foreach (var id in (PrerequisiteId[])[PrerequisiteId.DataFolder, PrerequisiteId.TailscaleInstalled, PrerequisiteId.TailscaleOnline, PrerequisiteId.TailscaleDnsName])
        {
            var blocked = new HostRun();
            blocked.Prerequisites.Fails = id;
            Assert.Equal(2, await blocked.RunAsync("--show-pairing", "--data-dir", data.Path));
            Assert.Equal([PrerequisiteTexts.Failure(id)], blocked.Lines);
        }

        // Settings from which no valid endpoint can be built.
        var noAddress = new HostRun();
        noAddress.Prerequisites.Settings = new RelaySettings { ConnectionMode = ConnectionMode.Public, PublicPathPrefix = "/not a prefix" };
        Assert.Equal(2, await noAddress.RunAsync("--show-pairing", "--data-dir", data.Path));
        Assert.Equal(["The PC address could not be resolved."], noAddress.Lines);
    }

    [Fact]
    public async Task Show_pairing_needs_an_interactive_console_on_the_default_folder()
    {
        // Redirected, on the default data folder: one fixed line, exit code 2, and nothing was composed
        // or read. The services of this run fail the test if they are asked for.
        using var output = new StringWriter();
        var exitCode = await HostProgram.RunAsync(
            ["--show-pairing"],
            _ => throw new InvalidOperationException("Nothing is composed before the console rule."),
            new HostOutput(output, IsRedirected: true),
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(HostProgram.PairingNeedsConsoleText + Environment.NewLine, output.ToString());
        Assert.DoesNotMatch(TokenShape(), output.ToString());
        Assert.DoesNotContain("token", output.ToString(), StringComparison.OrdinalIgnoreCase);

        // The same with an endpoint given: the rule is about the data folder, not about the address.
        using var withEndpoint = new StringWriter();
        Assert.Equal(2, await HostProgram.RunAsync(
            ["--show-pairing", "--endpoint", Endpoint],
            _ => throw new InvalidOperationException("Nothing is composed before the console rule."),
            new HostOutput(withEndpoint, IsRedirected: true),
            CancellationToken.None));
        Assert.Equal(HostProgram.PairingNeedsConsoleText + Environment.NewLine, withEndpoint.ToString());

        // With an explicit --data-dir a redirected run prints the payload: the manual GPU check reads
        // the throw-away pairing of its temporary folder this way.
        using var data = new TempFolder();
        await PairingMaintenance.ResetStoredPairingAsync(data.Path, CancellationToken.None);
        var redirected = new HostRun { Redirected = true };
        Assert.Equal(0, await redirected.RunAsync("--show-pairing", "--endpoint", Endpoint, "--data-dir", data.Path));
        Assert.Matches(PayloadShape(), redirected.Lines[0]);

        // An interactive console on the default folder gets past the rule. This run is stopped by a
        // failing data-folder check, so that the real data folder is never read by a test.
        var interactive = new HostRun { Redirected = false };
        interactive.Prerequisites.Fails = PrerequisiteId.DataFolder;
        Assert.Equal(2, await interactive.RunAsync("--show-pairing"));
        Assert.Equal(["LiveCue could not open its data folder."], interactive.Lines);
        Assert.Single(interactive.Prerequisites.Calls);

        // The rule is about --show-pairing only: every other run may be redirected.
        var checkOnly = new HostRun { Redirected = true };
        Assert.Equal(0, await checkOnly.RunAsync("--check-only"));
    }

    [Fact]
    public async Task Json_events()
    {
        var run = new HostRun();
        run.Mappings.Exposure = [new ExposedHandler(11443, "/", FunnelOn: false, Foreground: false, null)];
        var running = run.Start("--events", "json", "--port", "50123");
        var relay = await run.RunningRelayAsync();

        relay.Raise(new PhoneSeen(FakeRelay.Time));
        relay.Raise(new PhoneSeen(FakeRelay.Time));
        relay.Raise(new Notice(FakeRelay.Time, NoticeKind.PairingRestored, "Saved pairing restored."));
        relay.Raise(new RequestFailed(FakeRelay.Time, "request-0001", AnswerKind.Summary, "ASSISTANT_TIMEOUT", "The assistant took too long."));
        relay.Raise(new LocalModelChanged(
            FakeRelay.Time, new LocalModelStatus(LocalModelState.Ready, "nemotron", Busy: false, 12, "Model loaded."), "Model loaded."));
        relay.Raise(new EdgeStatistics(FakeRelay.Time, 3, 2, 1));
        await run.StopAsync();
        Assert.Equal(0, await running);

        // One JSON object per line, each with its type first.
        var lines = run.Lines.Select(line => JsonDocument.Parse(line).RootElement).ToList();
        Assert.All(run.Lines, line => Assert.StartsWith("{\"type\":\"", line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.Equal(JsonValueKind.Object, line.ValueKind));
        Assert.Equal(
            [
                "CheckingStarted", "PrerequisitesChecked", "RelayStarting", "RelayStarted", "ConnectionInspected",
                "PhoneSeen", "PhoneSeen", "Notice", "RequestFailed", "LocalModelChanged", "EdgeStatistics", "RelayStopped",
            ],
            lines.Select(line => line.GetProperty("type").GetString()));

        // The record's members, in camel case, enums as their names; the time comes right after the type.
        var started = lines[3];
        Assert.Equal(
            ["type", "at", "endpoint", "port", "mode", "protocolVersion", "relayVersion", "pairingWasCreated"],
            started.EnumerateObject().Select(member => member.Name));
        Assert.All(lines, line => Assert.Equal(["type", "at"], line.EnumerateObject().Take(2).Select(member => member.Name)));
        Assert.Equal(50123, started.GetProperty("port").GetInt32());
        Assert.Equal("https://pc.example.test:11443/", started.GetProperty("endpoint").GetString());
        Assert.Equal("Private", started.GetProperty("mode").GetString());
        Assert.Equal(2, started.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("1.0.0", started.GetProperty("relayVersion").GetString());
        Assert.Equal(FakeRelay.Time, started.GetProperty("at").GetDateTimeOffset());

        Assert.Equal("Passed", lines[1].GetProperty("report").GetProperty("results")[0].GetProperty("state").GetString());
        Assert.Equal("DataFolder", lines[1].GetProperty("report").GetProperty("results")[0].GetProperty("id").GetString());
        Assert.Equal(50123, lines[2].GetProperty("options").GetProperty("port").GetInt32());
        Assert.Equal(11443, lines[4].GetProperty("inspection").GetProperty("exposure")[0].GetProperty("httpsPort").GetInt32());
        Assert.Equal("PairingRestored", lines[7].GetProperty("kind").GetString());
        Assert.Equal("Summary", lines[8].GetProperty("kind").GetString());
        Assert.Equal("ASSISTANT_TIMEOUT", lines[8].GetProperty("code").GetString());
        Assert.Equal("Ready", lines[9].GetProperty("status").GetProperty("state").GetString());
        Assert.Equal(2, lines[10].GetProperty("tokenNotAccepted").GetInt64());
        Assert.Equal("Requested", lines[11].GetProperty("reason").GetString());

        // No line holds the token.
        Assert.DoesNotContain(FakeRelay.SyntheticToken, run.Output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotMatch(TokenShape(), run.Output.ToString());

        // In this form the host's own lines are JSON objects too: a failure with its text and exit code.
        var failed = new HostRun();
        failed.Prerequisites.Fails = PrerequisiteId.CodexFound;
        Assert.Equal(2, await failed.RunAsync("--events", "json"));
        var last = JsonDocument.Parse(failed.Lines[^1]).RootElement;
        Assert.Equal("Failure", last.GetProperty("type").GetString());
        Assert.Equal(PrerequisiteTexts.Failure(PrerequisiteId.CodexFound), last.GetProperty("message").GetString());
        Assert.Equal(2, last.GetProperty("exitCode").GetInt32());
        Assert.All(failed.Lines, line => Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(line).RootElement.ValueKind));
    }

    [Fact]
    public async Task Cancel_is_a_clean_stop()
    {
        var run = new HostRun();
        var running = run.Start(RunWithEndpoint);
        var relay = await run.RunningRelayAsync();

        relay.Raise(new PhoneSeen(FakeRelay.Time));
        relay.Raise(new PhoneSeen(FakeRelay.Time));
        relay.Raise(new AcceptingChanged(FakeRelay.Time, Accepting: false));
        relay.Raise(new PhoneSeen(FakeRelay.Time));
        await run.StopAsync();

        Assert.Equal(0, await running);
        Assert.Equal(
            [
                "Checking Codex and Tailscale...",
                "Starting the relay on 127.0.0.1:50123.",
                "Relay 1.0.0 started on 127.0.0.1:50123 (protocol 2). PC address: " + Endpoint,

                // Every request of a phone raises the event; one line says it until something else happens.
                "iPhone seen.",
                "Relay paused.",
                "iPhone seen.",
                "Relay stopped.",
            ],
            run.Lines);
        Assert.Equal(1, relay.StopCalls);
        Assert.True(relay.Disposed);
        Assert.Equal(RelayStatus.Stopped, relay.Status);

        // A cancel that arrives while the checks still run is a clean end as well, and starts nothing.
        var early = new HostRun();
        early.Prerequisites.Gate = new TaskCompletionSource();
        var checking = early.Start(RunWithEndpoint);
        await WaitForAsync(() => early.Prerequisites.Calls.Count == 1);
        await early.StopAsync();
        Assert.Equal(0, await checking);
        Assert.Empty(early.Relays.Created);
    }

    [Fact]
    public async Task A_relay_that_stops_by_itself_ends_the_host_with_exit_code_3()
    {
        var run = new HostRun();
        var running = run.Start(RunWithEndpoint);
        var relay = await run.RunningRelayAsync();

        relay.FaultWhileRunning();

        // Nobody pressed Ctrl+C: the host ends by itself, and it does not start the relay again.
        Assert.Equal(3, await running.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(
            ["Relay fault: INTERNAL.", "Relay stopped.", "The relay stopped after an internal error."],
            run.Lines.TakeLast(3));
        Assert.Single(run.Relays.Created);
        Assert.True(relay.Disposed);
    }

    [Fact]
    public async Task Conversation_text_needs_include_content()
    {
        var json = await ConversationRunAsync("--events", "json", "--port", "50123");
        var withContent = await ConversationRunAsync("--events", "json", "--include-content", "--port", "50123");
        var text = await ConversationRunAsync("--port", "50123");
        var textWithFlag = await ConversationRunAsync("--include-content", "--port", "50123");

        // By default no line carries conversation text, in either form.
        Assert.DoesNotContain(Canary, json.Output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, text.Output.ToString(), StringComparison.Ordinal);

        // The text form never prints it, whatever is asked for.
        Assert.DoesNotContain(Canary, textWithFlag.Output.ToString(), StringComparison.Ordinal);
        Assert.Equal(text.Lines, textWithFlag.Lines);

        // The events are still reported, without the five members.
        var started = Line(json, "RequestStarted");
        Assert.Equal("request-0001", started.GetProperty("requestId").GetString());
        Assert.Equal("Assist", started.GetProperty("kind").GetString());
        Assert.Equal("gpt-test", started.GetProperty("model").GetString());
        Assert.Equal("low", started.GetProperty("reasoningEffort").GetString());
        foreach (var member in (string[])["transcript", "partialTranscript", "instruction", "payloadJson"])
        {
            Assert.False(started.TryGetProperty(member, out _), member + " was printed.");
        }

        var completed = Line(json, "RequestCompleted");
        Assert.Equal(620.4, completed.GetProperty("durationMs").GetDouble());
        Assert.False(completed.TryGetProperty("payloadJson", out _));
        Assert.False(Line(json, "SpeechText").TryGetProperty("text", out _));
        Assert.Equal(["type", "at"], Line(json, "SpeechText").EnumerateObject().Select(member => member.Name));

        // With --include-content they are there.
        var startedWithContent = Line(withContent, "RequestStarted");
        Assert.Equal("First " + Canary, startedWithContent.GetProperty("transcript").GetString());
        Assert.Equal("Partial " + Canary, startedWithContent.GetProperty("partialTranscript").GetString());
        Assert.Equal("Instruction " + Canary, startedWithContent.GetProperty("instruction").GetString());
        Assert.Contains(Canary, startedWithContent.GetProperty("payloadJson").GetString(), StringComparison.Ordinal);
        Assert.Contains(Canary, Line(withContent, "RequestCompleted").GetProperty("payloadJson").GetString(), StringComparison.Ordinal);
        Assert.Equal("Spoken " + Canary, Line(withContent, "SpeechText").GetProperty("text").GetString());

        // The text form still says that something happened.
        Assert.Contains("Request request-0001 started: Assist, gpt-test, low.", text.Lines);
        Assert.Contains("Request request-0001 completed in 620 ms (Codex 600 ms).", text.Lines);
        Assert.Contains("Speech text received.", text.Lines);
    }

    [Fact]
    public async Task Exposure_refusal_is_exit_2()
    {
        var run = new HostRun();
        run.Mappings.Exposure = [new ExposedHandler(10000, "/livecue", FunnelOn: true, Foreground: false, null)];

        Assert.Equal(2, await run.RunAsync());

        Assert.Equal(
            [
                "Checking Codex and Tailscale...",
                "Tailscale mapping: private Absent, public Absent; 1 handler(s) forward to the relay port, 1 through Funnel.",
                "A public Tailscale mapping still points at this relay. Remove it, or switch to Public mode.",
            ],
            run.Lines);
        Assert.Empty(run.Relays.Created);

        // The JSON form carries the inspection that showed it, then the same text.
        var json = new HostRun();
        json.Mappings.Exposure = run.Mappings.Exposure;
        Assert.Equal(2, await json.RunAsync("--events", "json"));
        Assert.True(Line(json, "ConnectionInspected").GetProperty("inspection").GetProperty("hasFunnelExposure").GetBoolean());
        Assert.Equal(PrerequisiteTexts.ExposedInPrivateMode, Line(json, "Failure").GetProperty("message").GetString());
        Assert.Equal(2, Line(json, "Failure").GetProperty("exitCode").GetInt32());

        // In Public mode, and with an endpoint given, the same handler refuses nothing.
        var publicMode = new HostRun();
        publicMode.Mappings.Exposure = run.Mappings.Exposure;
        var running = publicMode.Start("--mode", "public");
        await publicMode.RunningRelayAsync();
        await publicMode.StopAsync();
        Assert.Equal(0, await running);
    }

    [Fact]
    public async Task A_stale_containment_check_only_warns()
    {
        // The version in use is not the one the containment check was last run for.
        HostRun Stale()
        {
            var run = new HostRun();
            run.Prerequisites.Settings = new RelaySettings { CodexContainmentCheckedVersion = "codex-cli 1.2.2" };
            return run;
        }

        // --check-only that passes: still exit code 0, with one warning line.
        var passed = Stale();
        Assert.Equal(0, await passed.RunAsync("--check-only"));
        Assert.Equal(["LiveCue Relay prerequisites passed.", StaleWarning], passed.Lines);

        // --check-only that fails for another reason: still exit code 2, its text, and the warning.
        var failed = Stale();
        failed.Prerequisites.Fails = PrerequisiteId.TailscaleOnline;
        Assert.Equal(2, await failed.RunAsync("--check-only"));
        Assert.Equal(["Tailscale is not online. Open Tailscale and connect first.", StaleWarning], failed.Lines);

        // A run: the relay starts, the warning is printed once, after the checks, and the exit is clean.
        var run = Stale();
        var running = run.Start(RunWithEndpoint);
        await run.RunningRelayAsync();
        await run.StopAsync();
        Assert.Equal(0, await running);
        Assert.Equal(["Checking Codex and Tailscale...", StaleWarning, "Starting the relay on 127.0.0.1:50123."], run.Lines.Take(3));
        Assert.Single(run.Lines, line => line == StaleWarning);

        // The JSON form says it as one object.
        var json = Stale();
        var jsonRunning = json.Start("--events", "json", "--endpoint", Endpoint);
        await json.RunningRelayAsync();
        await json.StopAsync();
        Assert.Equal(0, await jsonRunning);
        Assert.Equal(StaleWarning, Line(json, "Warning").GetProperty("message").GetString());
        Assert.Single(json.Lines, line => line.Contains("\"type\":\"Warning\"", StringComparison.Ordinal));

        // The other forms of the warning: never checked, and a version that could not be read.
        var never = new HostRun();
        never.Prerequisites.Settings = RelaySettings.Default;
        Assert.Equal(0, await never.RunAsync("--check-only"));
        Assert.Equal(
            "Codex is version codex-cli 1.2.3; the containment check has not been run yet. Run the containment check, then choose Mark as checked in Settings.",
            never.Lines[^1]);

        var unknown = new HostRun();
        unknown.Prerequisites.CodexVersion = null;
        Assert.Equal(0, await unknown.RunAsync("--check-only"));
        Assert.StartsWith("Codex's version could not be read; the containment check was last run for codex-cli 1.2.3.", unknown.Lines[^1], StringComparison.Ordinal);

        // Nothing is warned about when the version is the checked one, or when no Codex was found.
        var current = new HostRun();
        Assert.Equal(0, await current.RunAsync("--check-only"));
        Assert.Equal(["LiveCue Relay prerequisites passed."], current.Lines);

        var noCodex = Stale();
        noCodex.Prerequisites.Fails = PrerequisiteId.CodexFound;
        Assert.Equal(2, await noCodex.RunAsync("--check-only"));
        Assert.Equal([PrerequisiteTexts.Failure(PrerequisiteId.CodexFound)], noCodex.Lines);

        // --show-pairing ignores what the checks say about Codex: its output is the payload and the code.
        using var data = new TempFolder();
        await PairingMaintenance.ResetStoredPairingAsync(data.Path, CancellationToken.None);
        var pairing = Stale();
        Assert.Equal(0, await pairing.RunAsync("--show-pairing", "--endpoint", Endpoint, "--data-dir", data.Path));
        Assert.DoesNotContain(pairing.Lines, line => line.Contains("containment", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"[A-Za-z0-9_-]{43}")]
    private static partial Regex TokenShape();

    [GeneratedRegex(@"\A\{""endpoint"":""(?<endpoint>https://[^""]+)"",""token"":""(?<token>[A-Za-z0-9_-]{43})""\}\z")]
    private static partial Regex PayloadShape();

    /// <summary>The line of a JSON run whose type is <paramref name="type"/>.</summary>
    private static JsonElement Line(HostRun run, string type) => run.Lines
        .Select(line => JsonDocument.Parse(line).RootElement)
        .Single(line => line.GetProperty("type").GetString() == type);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "The host did not reach the expected state in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>A run in which a request with conversation text is answered and a speech text arrives.</summary>
    private static async Task<HostRun> ConversationRunAsync(params string[] args)
    {
        var run = new HostRun();
        var running = run.Start(args);
        var relay = await run.RunningRelayAsync();

        relay.Raise(new RequestStarted(
            FakeRelay.Time,
            "request-0001",
            AnswerKind.Assist,
            "gpt-test",
            "low",
            "First " + Canary,
            "Partial " + Canary,
            "Instruction " + Canary,
            "{\"requestId\":\"request-0001\",\"transcript\":\"First " + Canary + "\"}"));
        relay.Raise(new RequestCompleted(
            FakeRelay.Time, "request-0001", AnswerKind.Assist, 620.4, 600.1, "gpt-test", "low", "{\"answer\":\"Answer " + Canary + "\"}"));
        relay.Raise(new SpeechText(FakeRelay.Time, "Spoken " + Canary));
        await run.StopAsync();
        Assert.Equal(0, await running);
        return run;
    }

    /// <summary>One run of the host over scripted checks, scripted mappings and fake relays.</summary>
#pragma warning disable CA1001 // A run lives as long as its test; the token source and the writer hold no resource.
    private sealed class HostRun
#pragma warning restore CA1001
    {
        private readonly CancellationTokenSource _cancel = new();
        private bool _mappingsAreInvolved;

        public ScriptedPrerequisites Prerequisites { get; } = new();

        public ScriptedMappings Mappings { get; } = new();

        public FakeRelayFactory Relays { get; } = new();

        /// <summary>What the host printed. The writer may be read while the host still writes.</summary>
        public SharedOutput Output { get; } = new();

        /// <summary>True plays an output that goes to a file or a pipe.</summary>
        public bool Redirected { get; init; }

        /// <summary>The lines printed so far.</summary>
        public string[] Lines =>
            Output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Runs the host to its end.</summary>
        public Task<int> RunAsync(params string[] args) => Start(args).WaitAsync(TimeSpan.FromSeconds(30));

        /// <summary>Starts the host; the task ends when the host does.</summary>
        public Task<int> Start(params string[] args)
        {
            _mappingsAreInvolved = !args.Contains("--endpoint");
            return HostProgram.RunAsync(
                args,
                arguments => HostingFactory.Create(
                    new SupervisorOptions(arguments.DataDirectory ?? @"C:\LiveCue-tests\data", "1.0.0", arguments.Overrides),
                    new HostingOverrides
                    {
                        Files = new FakeHostFileSystem(),
                        Settings = new InMemorySettingsStore(Prerequisites.Settings),
                        Processes = new FakeProcessRunner(),
                        Prerequisites = Prerequisites,
                        Tailscale = new ScriptedTailscaleCli(),
                        Mappings = Mappings,
                        RelayFactory = Relays,
                    }),
                new HostOutput(Output, Redirected),
                _cancel.Token);
        }

        /// <summary>
        /// The relay of this run, once the start is over: the relay runs and, where a mapping inspection
        /// ends the start, that inspection has been printed. What a test raises next comes after it.
        /// </summary>
        public async Task<FakeRelay> RunningRelayAsync()
        {
            await WaitForAsync(() => Relays.Created.Count > 0 && Relays.Current.Status == RelayStatus.Running);
            if (_mappingsAreInvolved)
            {
                await WaitForAsync(() =>
                {
                    var printed = Output.ToString();
                    return printed.Contains("\"type\":\"ConnectionInspected\"", StringComparison.Ordinal)
                        || printed.Contains("Tailscale mapping:", StringComparison.Ordinal);
                });
            }

            return Relays.Current;
        }

        /// <summary>What Ctrl+C does.</summary>
        public Task StopAsync() => _cancel.CancelAsync();
    }

    /// <summary>What a host printed, kept in memory. It may be read while the host still writes.</summary>
    private sealed class SharedOutput : TextWriter
    {
        private readonly Lock _gate = new();
        private readonly StringBuilder _text = new();

        public override Encoding Encoding => Encoding.Unicode;

        public override void Write(char value)
        {
            lock (_gate)
            {
                _text.Append(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_gate)
            {
                _text.Append(value);
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                _text.Append(value).Append(Environment.NewLine);
            }
        }

        public override Task WriteLineAsync(string? value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _text.ToString();
            }
        }
    }

    /// <summary>Hands out the relays of another factory and remembers the last one.</summary>
    private sealed class CapturingRelayFactory(IRelayFactory inner) : IRelayFactory
    {
        private volatile IRelay? _created;

        public IRelay? Created => _created;

        public IRelay Create(RelayOptions options)
        {
            var relay = inner.Create(options);
            _created = relay;
            return relay;
        }
    }
}
