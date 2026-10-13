using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Processes;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>
/// The statements of protocol section 15 that their own area proves below the listener (against a class,
/// with a fake neighbour), proved here once more as the test plan's "done" asks: through a real loopback
/// listener, with what the statement is about being the production code. One test per group of
/// statements; the coverage table names each.
/// </summary>
public sealed class CatalogueStatementTests
{
    private static readonly string[] ExchangeMethods = ["initialize", "initialized", "model/list"];

    [Fact]
    public async Task P2_030_five_reads_and_five_validations_start_one_catalogue_process()
    {
        await using var relay = await ConformanceRelay.StartAsync();
        for (var read = 0; read < 5; read++)
        {
            Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Get, "/v1/models")).Status);
            Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody())).Status);
        }

        Assert.Single(relay.Codex.CatalogRequests);
    }

    [Fact]
    public async Task P2_031_P2_032_a_failing_read_returns_the_last_good_catalogue_unchanged()
    {
        // The cache life is on the relay's clock; the catalogue processes are real.
        var clock = new ManualTimeProvider();
        await using var relay = await ConformanceRelay.StartAsync(options => options.Clock = clock);
        var good = await relay.ExchangeAsync(HttpMethod.Get, "/v1/models");
        Assert.Equal(200, good.Status);

        // The loader starts failing, and the cached result has aged out: the next read runs it again.
        relay.Codex.CatalogScenario = "error";
        clock.Advance(ProtocolConstants.CatalogCacheLife + TimeSpan.FromSeconds(1));
        var again = await PumpAsync(clock, relay.ExchangeAsync(HttpMethod.Get, "/v1/models"));
        var answer = await PumpAsync(clock, relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody()));

        // The same catalogue, byte for byte (P2-032: nothing a caller did to a result changed the next),
        // and the selection still validates.
        Assert.Equal(good, again);
        Assert.Equal(200, answer.Status);
        Assert.True(relay.Codex.CatalogRequests.Count > 1, "The loader was not asked again.");
    }

    [Fact]
    public async Task P2_035_one_failure_then_success_takes_two_loader_calls()
    {
        // The catalogue seam is the fake here (no stand-in scenario returns an empty list); the reader,
        // the cache and the routes are the production ones.
        await using var host = await RelayTestHost.StartAsync(options => options.UseSystemClock = true);
        host.Catalog.Loader = () => FakeCatalogSource.FixtureL;
        host.Catalog.FailuresLeft = 1;

        // P2-035: one read, two loader calls, entry 0 is the fixture.
        using (var first = await host.GetAsync("/v1/models"))
        {
            Assert.Equal("""{"models":[{"id":"gpt-6-luna","name":"GPT-6 Luna","reasoningEfforts":["low"]}]}""", await first.Content.ReadAsStringAsync());
        }

        Assert.Equal(2, host.Catalog.Calls);
    }

    [Fact]
    public async Task P2_033_an_empty_catalogue_is_served_empty_and_validates_nothing()
    {
        await using var host = await RelayTestHost.StartAsync();
        using (var before = await host.PostAsync("/v1/assist", Bodies.Assist()))
        {
            Assert.Equal(200, (int)before.StatusCode);
        }

        host.Catalog.Loader = () => [];
        host.Clock.Advance(ProtocolConstants.CatalogCacheLife + TimeSpan.FromSeconds(1));
        using var models = await host.GetAsync("/v1/models");
        using var assist = await host.PostAsync("/v1/assist", Bodies.Assist("request-0002"));

        Assert.Equal("""{"models":[]}""", await models.Content.ReadAsStringAsync());
        await Wire.AssertErrorAsync(assist, 400, "ASSISTANT_SELECTION_UNAVAILABLE");
        Assert.Single(host.Backend.Runs);
    }

    [Fact]
    public async Task P2_034_without_any_good_catalogue_there_is_no_fallback_list()
    {
        await using var relay = await ConformanceRelay.StartAsync();
        relay.Codex.CatalogScenario = "early-exit";

        var models = await relay.ExchangeAsync(HttpMethod.Get, "/v1/models");
        var assist = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody());

        Assert.Equal(503, models.Status);
        Assert.Equal(
            """{"error":"The PC could not read Codex's model list. Keep Codex signed in, then retry. Your selected model has not changed.","code":"ASSISTANT_CATALOG_UNAVAILABLE"}""",
            models.Body);
        Assert.Equal(models, assist);
        Assert.Empty(relay.Codex.ExecRequests);
    }

    [Fact]
    public async Task P2_038_P2_039_P2_040_the_offered_list_is_the_filtered_one()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Catalog.Loader = () =>
        [
            new RawCatalogEntry("gpt-5.6-sol", "Sol", Hidden: false, ["low", "high", "ultra"]),
            new RawCatalogEntry("gpt-5.6-terra", "Terra", Hidden: false, ["low", "high", "ultra"]),
            new RawCatalogEntry("gpt-5.6-luna", "Luna", Hidden: false, ["low", "high", "ultra"]),
            new RawCatalogEntry("gpt-5.5", "Five five", Hidden: false, ["low", "high", "ultra"]),
            new RawCatalogEntry("gpt-6-astra", "Astra", Hidden: false, ["low", "medium", "ultra"]),
            new RawCatalogEntry("gpt-5.3-codex-spark", "Spark", Hidden: false, ["low", "medium", "ultra"]),
            new RawCatalogEntry("gpt-hidden", "Hidden", Hidden: true, ["low"]),
            new RawCatalogEntry("not-a-gpt", "Other", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-UPPER", "Upper", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-" + new string('a', 61), "Too long", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-no-efforts", "None left", Hidden: false, ["ultra"]),
            new RawCatalogEntry("gpt-nameless", null, Hidden: false, ["low"]),
        ];

        using var response = await host.GetAsync("/v1/models");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var offered = document.RootElement.GetProperty("models").EnumerateArray()
            .Select(model => (
                Id: model.GetProperty("id").GetString(),
                Name: model.GetProperty("name").GetString(),
                Efforts: string.Join(',', model.GetProperty("reasoningEfforts").EnumerateArray().Select(effort => effort.GetString()))))
            .ToList();

        Assert.Equal(
            [
                ("gpt-5.6-sol", "Sol", "none,low,high"),
                ("gpt-5.6-terra", "Terra", "none,low,high"),
                ("gpt-5.6-luna", "Luna", "none,low,high"),
                ("gpt-5.5", "Five five", "none,low,high"),
                ("gpt-6-astra", "Astra", "low,medium"),
                ("gpt-5.3-codex-spark", "Spark", "low,medium"),
                ("gpt-nameless", "gpt-nameless", "low"),
            ],
            offered);
    }

    [Fact]
    public async Task P2_041_the_validation_cases_of_the_statement()
    {
        await using var host = await RelayTestHost.StartAsync();

        async Task<(int Status, string Body)> AssistAsync(string assistant)
        {
            using var response = await host.PostAsync("/v1/assist", $$"""{"requestId":"request-0001","assistant":{{assistant}}}""");
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        var valid = await AssistAsync("""{"model":"gpt-test","reasoningEffort":"low"}""");
        Assert.Equal(200, valid.Status);
        Assert.Equal(("gpt-test", "low"), (host.Backend.Runs[0].Model, host.Backend.Runs[0].ReasoningEffort));

        foreach (var malformed in (string[])["null", "[]", "\"model\""])
        {
            Assert.Contains("\"code\":\"INVALID_ASSISTANT\"", (await AssistAsync(malformed)).Body, StringComparison.Ordinal);
        }

        foreach (var unknown in (string[])["""{"model":"--config","reasoningEffort":"low"}""", """{"model":"gpt-test","reasoningEffort":"low\";shell=true"}"""])
        {
            Assert.Contains("\"code\":\"ASSISTANT_SELECTION_UNAVAILABLE\"", (await AssistAsync(unknown)).Body, StringComparison.Ordinal);
        }

        Assert.Single(host.Backend.Runs);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("early-exit")]
    [InlineData("error")]
    [InlineData("flood")]
    [InlineData("silent")]
    public async Task P2_044_the_catalogue_exchange_with_a_real_process(string scenario)
    {
        var clock = new ManualTimeProvider();
        await using var relay = await ConformanceRelay.StartAsync(options => options.Clock = clock);
        relay.Codex.CatalogScenario = scenario;

        var pending = relay.ExchangeAsync(HttpMethod.Get, "/v1/models");
        if (scenario == "silent")
        {
            // "No reply within the per-attempt limit": each attempt's process is let start and read its
            // first line; then the limit passes on the clock, and the retry gap after it.
            for (var attempt = 1; attempt <= ProtocolConstants.CatalogAttempts; attempt++)
            {
                await Wire.UntilAsync(() => relay.Codex.CatalogReportFiles.Count == attempt, "the attempt began", TimeSpan.FromSeconds(30));
                await TestChild.WaitForLinesAsync(relay.Codex.CatalogReportFiles[attempt - 1], 2);
                Assert.False(pending.IsCompleted);
                clock.Advance(ProtocolConstants.CatalogAttemptTimeout);
                var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                while (!pending.IsCompleted && relay.Codex.CatalogReportFiles.Count == attempt)
                {
                    Assert.True(DateTime.UtcNow < until, "The attempt did not end at its limit.");
                    clock.Advance(TimeSpan.FromMilliseconds(10));
                    await Task.Delay(5);
                }
            }
        }

        var models = await PumpAsync(clock, pending);

        if (scenario == "ok")
        {
            // The entries received before the cursor are the catalogue; the entry without a model is skipped.
            Assert.Equal(200, models.Status);
            Assert.Equal("""{"models":[{"id":"gpt-stand-in","name":"Stand-in","reasoningEfforts":["low","high"]}]}""", models.Body);
            Assert.Single(relay.Codex.CatalogRequests);
        }
        else
        {
            // A failed read, tried C-44 times.
            Assert.Equal(503, models.Status);
            Assert.Contains("ASSISTANT_CATALOG_UNAVAILABLE", models.Body, StringComparison.Ordinal);
            Assert.Equal(ProtocolConstants.CatalogAttempts, relay.Codex.CatalogRequests.Count);
        }

        // What each process was sent, and that it is gone: the three lines of protocol 12.1, in order.
        foreach (var (file, request) in relay.Codex.CatalogReportFiles.Zip(relay.Codex.CatalogRequests))
        {
            Assert.Equal(["app-server", "--listen", "stdio://"], request.Arguments);
            Assert.Equal(EnvironmentPolicy.CodexAllowlist, request.Environment);
            var lines = await TestChild.WaitForLinesAsync(file, 2);
            await TestChild.WaitUntilGoneAsync(int.Parse(lines[0], System.Globalization.CultureInfo.InvariantCulture));
            var sent = Encoding.UTF8.GetString(TestChild.ReadShared(file))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(line =>
                {
                    using var message = JsonDocument.Parse(line);
                    return message.RootElement.GetProperty("method").GetString();
                })
                .ToList();
            Assert.Equal(ExchangeMethods.Take(sent.Count), sent);
            Assert.InRange(sent.Count, 1, 3);
            if (scenario is "ok" or "error" or "early-exit")
            {
                Assert.Equal(3, sent.Count);
            }
        }
    }

    /// <summary>Advances the manual clock, a little at a time, until the work is done.</summary>
    internal static async Task<T> PumpAsync<T>(ManualTimeProvider clock, Task<T> work, TimeSpan? step = null)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!work.IsCompleted)
        {
            Assert.True(DateTime.UtcNow < until, "The awaited work did not end in time.");
            clock.Advance(step ?? TimeSpan.FromMilliseconds(25));
            await Task.Delay(5);
        }

        return await work;
    }
}

/// <summary>Protocol 15.5, the statements about the controller and the lab runtime, through the listener.</summary>
public sealed class LocalModelStatementTests
{
    [Fact]
    public async Task P2_080_to_P2_085_the_sequence_of_the_statements()
    {
        await using var relay = await ConformanceRelay.StartAsync();

        // P2-080: a new controller is stopped, and all five members have their documented types.
        var status = await StatusAsync(relay);
        Assert.Equal(JsonValueKind.String, status.GetProperty("state").ValueKind);
        Assert.Equal("stopped", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("model").ValueKind);
        Assert.Equal(JsonValueKind.False, status.GetProperty("busy").ValueKind);
        Assert.True(status.GetProperty("elapsedSeconds").TryGetInt32(out _));
        Assert.Equal(JsonValueKind.String, status.GetProperty("message").ValueKind);
        Assert.Equal(5, status.EnumerateObject().Count());

        // P2-081: start with the gate closed is 202 "starting"; the status after it is still "starting"
        // and is given without a probe.
        relay.Lab.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("nemotron"));
        Assert.Equal(202, started.Status);
        Assert.Equal("starting", EndToEndRouteTests.StateOf(started.Body));
        await Wire.UntilAsync(() => relay.Lab.StartCalls == 1, "the start began");
        var probes = relay.Lab.ProbeCalls;
        await Task.Delay(5);
        status = await StatusAsync(relay);
        Assert.Equal("starting", status.GetProperty("state").GetString());
        Assert.Equal("nemotron", status.GetProperty("model").GetString());
        Assert.Equal(probes, relay.Lab.ProbeCalls);

        // P2-082: a second command while it runs.
        foreach (var (target, model) in ((string, string)[])[("/v1/local-model/start", "qwen3"), ("/v1/local-model/stop", "nemotron")])
        {
            var refused = await relay.ExchangeAsync(HttpMethod.Post, target, Bodies.LocalModel(model));
            Assert.Equal(409, refused.Status);
            Assert.Equal(
                """{"error":"A PC model operation is already in progress. Wait for it to finish.","code":"MODEL_OPERATION_IN_PROGRESS"}""",
                refused.Body);
        }

        // P2-083: the gate opens; the model is nemotron; a prepare for it (a stream) starts nothing again.
        relay.Lab.StartGate.SetResult();
        await EndToEndRouteTests.UntilStateAsync(relay, "ready");
        Assert.Equal("nemotron", (await StatusAsync(relay)).GetProperty("model").GetString());
        using (var phone = await relay.OpenSpeechAsync("nemotron"))
        {
            await phone.WaitForAsync("ready");
            await phone.SendEndStreamAsync();
            await phone.ReadToEndAsync();
        }

        Assert.Equal(1, relay.Lab.StartCalls);

        // P2-084: starting the other model switches.
        relay.Lab.StartGate = null;
        var switched = await EndToEndRouteTests.SendUntilNotBusyAsync(relay, "/v1/local-model/start", Bodies.LocalModel("qwen3"));
        Assert.Equal(202, switched.Status);
        await EndToEndRouteTests.UntilStateAsync(relay, "ready");
        Assert.Equal("qwen3", (await StatusAsync(relay)).GetProperty("model").GetString());
        Assert.Equal(["nemotron", "qwen3"], relay.Lab.StartedModels);

        // P2-085: stop ends stopped, with one stop call.
        Assert.Equal(202, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/stop", Bodies.LocalModel("qwen3"))).Status);
        await EndToEndRouteTests.UntilStateAsync(relay, "stopped");
        Assert.Equal(1, relay.Lab.StopCalls);
    }

    [Fact]
    public async Task P2_086_a_failed_start_is_an_error_without_private_text_and_the_latch_expires()
    {
        var clock = new ManualTimeProvider();
        await using var relay = await ConformanceRelay.StartAsync(options => options.Clock = clock);
        relay.Lab.ProbeFails = true;
        relay.Lab.StartThrowsPrivateText = true;

        Assert.Equal(202, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("nemotron"))).Status);
        await EndToEndRouteTests.UntilStateAsync(relay, "error");
        var failed = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
        Assert.Equal(FixedTexts.ModelStartFailed, MessageOf(failed.Body));
        Assert.DoesNotContain("PRIVATE", failed.Body, StringComparison.Ordinal);

        // After C-48 on the injected clock the latch has expired: stopped, with MSG-3.
        clock.Advance(ProtocolConstants.ModelErrorLatch);
        var expired = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
        Assert.Equal("stopped", EndToEndRouteTests.StateOf(expired.Body));
        Assert.Equal(FixedTexts.ModelStopped, MessageOf(expired.Body));

        // A new start clears a latch at once.
        Assert.Equal(202, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("nemotron"))).Status);
        await EndToEndRouteTests.UntilStateAsync(relay, "error");
        relay.Lab.StartThrowsPrivateText = false;
        relay.Lab.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var again = await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("nemotron"));
        Assert.Equal(202, again.Status);
        Assert.Equal("starting", EndToEndRouteTests.StateOf(again.Body));
        Assert.Equal("starting", EndToEndRouteTests.StateOf((await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model")).Body));
        relay.Lab.StartGate.SetResult();

        await relay.Events.SettleAsync();
        PrivateText.AssertAbsent(relay, relay.Events.Snapshot().Select(PrivateText.Serialise));
    }

    [Theory]
    [InlineData("exit 0")]
    [InlineData("exit 3")]
    [InlineData("time limit")]
    public async Task P2_095_the_production_runtime_asks_for_the_relays_own_script_copies(string outcome)
    {
        // The production lab runtime behind the listener. Its health probe dials a stub of this test; its
        // launcher scripts are requested from the process runner, which records the request and starts
        // nothing: no PowerShell runs here.
        var loaded = (string?)null;
        await using var lab = new LabHealthStub((stream, ct) => (loaded is null
            ? LabHealthStub.Hangup()
            : LabHealthStub.Reply($$"""{"ready":true,"model":"{{loaded}}","busy":false,"relayProtocol":1}"""))(stream, ct));
        await using var relay = await ConformanceRelay.StartAsync(options =>
        {
            options.ProductionLabRuntime = true;
            options.LabPort = lab.Port;
        });
        relay.Codex.LabScript = request =>
        {
            if (outcome == "exit 0")
            {
                loaded = request.Arguments.Contains("-Model") ? request.Arguments[request.Arguments.ToList().IndexOf("-Model") + 1] : null;
            }

            return outcome switch
            {
                "exit 0" => new ProcessResult(ProcessOutcome.Exited, 0, ReadOnlyMemory<byte>.Empty),
                "exit 3" => new ProcessResult(ProcessOutcome.Exited, 3, ReadOnlyMemory<byte>.Empty),
                _ => new ProcessResult(ProcessOutcome.TimedOut, -1, ReadOnlyMemory<byte>.Empty),
            };
        };

        // A launcher that was changed on disk after the start is not what runs.
        var copy = LabScripts.PathFor(relay.DataDirectory, LabScripts.StartFileName);
        await File.WriteAllTextAsync(copy, "Write-Output 'changed'");

        Assert.Equal(202, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("qwen3"))).Status);
        await EndToEndRouteTests.UntilStateAsync(relay, outcome == "exit 0" ? "ready" : "error");

        var start = Assert.Single(relay.Codex.LabRequests);
        Assert.Equal(LabScripts.PowerShellPath, start.FileName);
        Assert.Equal(LabScripts.StartArguments(relay.DataDirectory, relay.Options.LabFolder!, "qwen3", lab.Port), start.Arguments);
        Assert.Equal(EnvironmentPolicy.LabScriptAllowlist, start.Environment);
        Assert.Equal(ProtocolConstants.LabStartTimeout, start.Timeout);
        Assert.Null(start.StandardInput);

        // The file named after -File is the relay's own copy under <data folder>\lab\, never a file of the
        // lab folder, and it holds the embedded bytes again.
        var file = start.Arguments[start.Arguments.ToList().IndexOf("-File") + 1];
        Assert.Equal(copy, file);
        Assert.StartsWith(Path.Combine(relay.DataDirectory, "lab") + Path.DirectorySeparatorChar, file, StringComparison.Ordinal);
        Assert.False(file.StartsWith(relay.Options.LabFolder!, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LabScripts.Embedded(LabScripts.StartFileName).ToArray(), await File.ReadAllBytesAsync(copy));

        if (outcome != "exit 0")
        {
            var failed = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
            Assert.Equal(FixedTexts.ModelStartFailed, MessageOf(failed.Body));
            return;
        }

        // The stop script, with its own arguments.
        Assert.Equal(202, (await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/stop", Bodies.LocalModel("qwen3"))).Status);
        await Wire.UntilAsync(() => relay.Codex.LabRequests.Count == 2, "the stop script was asked for");
        var stop = relay.Codex.LabRequests[1];
        Assert.Equal(LabScripts.PowerShellPath, stop.FileName);
        Assert.Equal(LabScripts.StopArguments(relay.DataDirectory, lab.Port), stop.Arguments);
        Assert.Equal(EnvironmentPolicy.LabScriptAllowlist, stop.Environment);
        Assert.Equal(ProtocolConstants.LabStopTimeout, stop.Timeout);
    }

    private static string? MessageOf(string statusJson)
    {
        using var document = JsonDocument.Parse(statusJson);
        return document.RootElement.GetProperty("message").GetString();
    }

    private static async Task<JsonElement> StatusAsync(ConformanceRelay relay)
    {
        var (status, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
        Assert.Equal(200, status);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}

/// <summary>Protocol 15.7, the statements about the token and its storage, with real files and real DPAPI.</summary>
public sealed class PairingStatementTests
{
    [Fact]
    public async Task P2_140_P2_141_P2_142_the_stored_pairing_and_the_shown_payload()
    {
        using var data = new TempFolder();
        string token;
        byte[] file;
        await using (var relay = await ConformanceRelay.StartAsync(options => options.DataDirectory = data.Path))
        {
            // P2-141: a pairing created in an empty folder.
            token = relay.Token;
            Assert.Matches(@"\A[A-Za-z0-9_-]{43}\z", token);
            Assert.True((await relay.Events.WaitForAsync<RelayStarted>()).PairingWasCreated);
            file = await File.ReadAllBytesAsync(Path.Combine(data.Path, "pairing.json"));
            using (var document = JsonDocument.Parse(file))
            {
                var hash = document.RootElement.GetProperty("tokenHash").GetString()!;
                Assert.Matches(@"\A[0-9a-f]{64}\z", hash);
                Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("protectedToken").ValueKind);
                Assert.DoesNotContain(document.RootElement.EnumerateObject(), member => member.Value.ValueKind == JsonValueKind.String && member.Value.GetString() == token);

                // P2-140: the hash is the hash of the token, and does not contain it; the token it
                // verifies is served and another is not.
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token))), hash);
                Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
            }

            Assert.DoesNotContain(token, Encoding.UTF8.GetString(file), StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(Encoding.ASCII.GetBytes(token)), Encoding.UTF8.GetString(file), StringComparison.Ordinal);
            Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Get, "/v1/health")).Status);
            Assert.Equal(401, (await relay.ExchangeAsync(relay.Request(HttpMethod.Get, "/v1/health", token: TestTokens.WellFormedUnknown))).Status);

            // P2-142: showing twice leaves the file byte-identical, and the payload is the text of 4.2.
            var first = await relay.Relay.ShowPairingAsync(CancellationToken.None);
            var second = await relay.Relay.ShowPairingAsync(CancellationToken.None);
            Assert.Equal(file, await File.ReadAllBytesAsync(Path.Combine(data.Path, "pairing.json")));
            var payload = await ((IPairingExport)relay.Relay).GetPairingPayloadAsync(CancellationToken.None);
            Assert.Equal($$"""{"endpoint":"https://pc.example.test:11443/","token":"{{token}}"}""", payload);

            // P2-144: the matrix handed to the desktop is square, at least 21 rows, and is the encoding of
            // exactly that text (the encoder itself is proved against an independent one in QrMatrixTests);
            // the object that carries it has no token member.
            Assert.Equal("https://pc.example.test:11443/", first.Endpoint);
            Assert.True(first.Modules.Length >= 21);
            Assert.All(first.Modules, row => Assert.Equal(first.Modules.Length, row.Length));
            Assert.Equal(QrMatrix.Encode(Encoding.UTF8.GetBytes(payload)), first.Modules);
            Assert.Equal(first.Modules, second.Modules);
            Assert.DoesNotContain(typeof(PairingQr).GetProperties(), property => property.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
        }

        // P2-142: loading again returns the same token, and the file is still the same bytes.
        await using var again = await ConformanceRelay.StartAsync(options => options.DataDirectory = data.Path);
        Assert.Equal(token, again.Token);
        Assert.False((await again.Events.WaitForAsync<RelayStarted>()).PairingWasCreated);
        Assert.Equal(file, await File.ReadAllBytesAsync(Path.Combine(data.Path, "pairing.json")));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("a malformed hash")]
    [InlineData("a hash of another token")]
    [InlineData("a protected copy that is not ours")]
    [InlineData("an empty file")]
    public async Task P2_146_an_unreadable_pairing_file_refuses_the_start_and_is_left_untouched(string what)
    {
        using var data = new TempFolder();
        var path = Path.Combine(data.Path, "pairing.json");
        await (await ConformanceRelay.StartAsync(options => options.DataDirectory = data.Path)).DisposeAsync();

        var good = await File.ReadAllTextAsync(path);
        using var parsed = JsonDocument.Parse(good);
        var hash = parsed.RootElement.GetProperty("tokenHash").GetString()!;
        var copy = parsed.RootElement.GetProperty("protectedToken").GetString()!;
        var bad = what switch
        {
            "not JSON" => "this is not a pairing file",
            "a malformed hash" => good.Replace(hash, hash[..63] + "G", StringComparison.Ordinal),
            "a hash of another token" => good.Replace(hash, Convert.ToHexStringLower(SHA256.HashData("another"u8)), StringComparison.Ordinal),
            "a protected copy that is not ours" => good.Replace(copy, Convert.ToBase64String(RandomNumberGenerator.GetBytes(200)), StringComparison.Ordinal),
            _ => string.Empty,
        };
        Assert.NotEqual(good, bad);
        await File.WriteAllTextAsync(path, bad);
        var before = await File.ReadAllBytesAsync(path);

        var failure = await Assert.ThrowsAsync<RelayStartException>(
            () => ConformanceRelay.StartAsync(options => options.DataDirectory = data.Path));

        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, failure.Code);
        Assert.Equal("The saved pairing could not be read.", failure.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".pending"));
    }

    [Theory]
    [InlineData(SelfCheckResult.Verified)]
    [InlineData(SelfCheckResult.VerifiedThroughFunnel)]
    [InlineData(SelfCheckResult.NotVerified)]
    public async Task P2_147_the_self_check_request_is_verified_and_is_not_a_phone(SelfCheckResult expected)
    {
        // The request the relay asks its self-check client to make is made here against the relay's own
        // listener, with exactly the three headers of protocol 4.5. For "not verified" it is made with a
        // token that is not accepted, as it would be when the advertised address is somebody else's relay.
        await using var relay = await ConformanceRelay.StartAsync(options => options.Mode = ConnectionMode.Public);
        relay.SelfCheck.Handler = async (call, ct) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                TestTokens.Bearer(expected == SelfCheckResult.NotVerified ? TestTokens.WellFormedUnknown : call.Token));
            request.Headers.TryAddWithoutValidation("X-LiveCue-Protocol", "2");
            request.Headers.TryAddWithoutValidation("X-LiveCue-Self-Check", call.Nonce);
            if (expected == SelfCheckResult.VerifiedThroughFunnel)
            {
                request.Headers.TryAddWithoutValidation("Tailscale-Funnel-Request", "?1");
            }

            using var response = await relay.Client.SendAsync(request, ct);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return (int)response.StatusCode == 200
                && document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
                && document.RootElement.GetProperty("protocol").GetInt32() == 2;
        };

        await relay.Relay.ShowPairingAsync(CancellationToken.None);
        var completed = await relay.Events.WaitForAsync<SelfCheckCompleted>();
        await relay.Events.SettleAsync();

        var call = Assert.Single(relay.SelfCheck.Calls);
        Assert.Equal(new Uri("https://pc.example.test:11443/v1/health"), call.HealthUrl);
        Assert.Equal(relay.Token, call.Token);
        Assert.Matches(@"\A[A-Za-z0-9_-]{22}\z", call.Nonce);
        Assert.Equal(expected, completed.Result);

        // No presence event results: the request with the current nonce is the one request that is not a
        // phone, and the one that was refused never was one.
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
    }

    [Theory]
    [InlineData("http://pc.example.test:11443/")]
    [InlineData("HTTPS://pc.example.test:11443/")]
    [InlineData("https://pc.example.test:11443/?x=1")]
    [InlineData("https://pc.example.test:11443//")]
    [InlineData("https://pc.example.test:11443/a/../b/")]
    [InlineData("https://pc.example.test:11443/a/b/c/d/e/")]
    [InlineData("https://pc.example.test:11443/\n")]
    [InlineData("")]
    public async Task P2_148_a_bad_advertised_endpoint_is_a_start_fault(string endpoint)
    {
        var failure = await Assert.ThrowsAsync<RelayStartException>(
            () => ConformanceRelay.StartAsync(options => options.Endpoint = endpoint));

        Assert.Equal(RelayFaultCode.InvalidEndpoint, failure.Code);
    }

    [Fact]
    public async Task P2_148_an_endpoint_without_its_slash_is_advertised_in_canonical_form()
    {
        await using var relay = await ConformanceRelay.StartAsync(options => options.Endpoint = "https://pc.example.test:11443/livecue");

        Assert.Equal("https://pc.example.test:11443/livecue/", Assert.Single(relay.Events.OfType<RelayStarted>()).Endpoint);
        var payload = await ((IPairingExport)relay.Relay).GetPairingPayloadAsync(CancellationToken.None);
        Assert.StartsWith("""{"endpoint":"https://pc.example.test:11443/livecue/","token":""", payload, StringComparison.Ordinal);
        Assert.Equal("https://pc.example.test:11443/livecue/", (await relay.Relay.ShowPairingAsync(CancellationToken.None)).Endpoint);
        await Wire.UntilAsync(() => relay.SelfCheck.Calls.Count == 1, "the self-check began");
        Assert.Equal(new Uri("https://pc.example.test:11443/livecue/v1/health"), relay.SelfCheck.Calls[0].HealthUrl);

        // The prefix is the proxy's: the relay itself serves the bare routes and not the prefixed ones.
        Assert.Equal(200, (await relay.ExchangeAsync(HttpMethod.Get, "/v1/health")).Status);
        Assert.Equal(404, (await relay.ExchangeAsync(HttpMethod.Get, "/livecue/v1/health")).Status);
    }
}
