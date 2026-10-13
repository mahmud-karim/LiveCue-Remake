using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using LiveCue.Relay;
using LiveCue.Relay.Processes;
using LiveCue.TestSupport;
using Xunit.Abstractions;

namespace LiveCue.Hosting.Tests.LiveSmoke;

/// <summary>
/// L-1 of the test plan (section 3): a real Codex answer through the relay. Opt-in and never run in CI:
///
/// <code>
/// $env:LIVECUE_LIVE_CODEX = "1"
/// &amp; $env:LIVECUE_DOTNET_EXE test windows/tests/LiveCue.Hosting.Tests -c Release --filter "Category=Live"
/// </code>
///
/// It costs one catalogue read and two small model requests on the Codex sign-in of this PC. The input
/// is synthetic: never a microphone, never a real conversation. A failure is reported, not retried.
/// </summary>
[Trait("Category", "Live")]
public sealed class CodexLiveTests(ITestOutputHelper output)
{
    private const string Transcript =
        "Our team is choosing between transcribing speech on the phone and sending audio to a server. "
        + "What is the main advantage of keeping the transcription on the phone?";

    [LiveFact("LIVECUE_LIVE_CODEX")]
    [Trait("Category", "Live")]
    public async Task Catalogue_assist_and_summary_through_the_relay()
    {
        var codexBefore = LiveCodexRelay.CodexProcessIds();
        await using var live = await LiveCodexRelay.StartAsync();

        // GET /v1/models: the first offered model and its lowest effort.
        var selection = await live.FirstSelectionAsync();

        // POST /v1/assist: status 200 and the shape of protocol 8.3.
        var (assistStatus, assistBody) = await live.PostAsync("/v1/assist", selection, Transcript);
        Assert.Equal(HttpStatusCode.OK, assistStatus);
        using var assist = JsonDocument.Parse(assistBody);
        var answer = assist.RootElement;
        Assert.Equal(JsonValueKind.String, answer.GetProperty("detectedQuestion").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(answer.GetProperty("answer").GetString()));
        Assert.Equal(JsonValueKind.String, answer.GetProperty("details").ValueKind);
        var memory = answer.GetProperty("memory");
        Assert.Equal(JsonValueKind.String, memory.GetProperty("summary").ValueKind);
        Assert.Equal(JsonValueKind.Array, memory.GetProperty("facts").ValueKind);
        Assert.Equal(JsonValueKind.Array, memory.GetProperty("openQuestions").ValueKind);
        Assert.Equal(JsonValueKind.Null, memory.GetProperty("throughSegmentId").ValueKind);
        var assistCodexMs = AssertExecution(answer.GetProperty("execution"), selection);

        // POST /v1/session-summary: status 200 and its shape.
        var (summaryStatus, summaryBody) = await live.PostAsync("/v1/session-summary", selection, Transcript);
        Assert.Equal(HttpStatusCode.OK, summaryStatus);
        using var summaryDocument = JsonDocument.Parse(summaryBody);
        var summary = summaryDocument.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(summary.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(summary.GetProperty("summary").GetString()));
        Assert.Equal(JsonValueKind.Array, summary.GetProperty("keyPoints").ValueKind);
        Assert.Equal(JsonValueKind.Array, summary.GetProperty("actionItems").ValueKind);
        var summaryCodexMs = AssertExecution(summary.GetProperty("execution"), selection);

        // The per-request folders are gone, and no Codex process of this run remains.
        Assert.Empty(Directory.EnumerateFileSystemEntries(live.WorkFolder));
        await live.StopAsync();
        var remaining = await RemainingCodexAsync(codexBefore);
        Assert.True(remaining == 0, "A Codex process of this run is still alive.");

        output.WriteLine($"Codex version: {live.CodexVersion ?? "unknown"}");
        output.WriteLine($"Selection: {selection.Model}, {selection.Effort}");
        output.WriteLine($"Assist: {assistCodexMs:0} ms in Codex");
        output.WriteLine($"Summary: {summaryCodexMs:0} ms in Codex");
    }

    /// <summary>The <c>execution</c> member of protocol 8.3: the echo of the selection and the timings.</summary>
    private static double AssertExecution(JsonElement execution, (string Model, string Effort) selection)
    {
        Assert.Equal(selection.Model, execution.GetProperty("model").GetString());
        Assert.Equal(selection.Effort, execution.GetProperty("reasoningEffort").GetString());
        var codexMs = execution.GetProperty("codexMs").GetDouble();
        var relayTotalMs = execution.GetProperty("relayTotalMs").GetDouble();
        Assert.True(codexMs > 0, "codexMs is not above zero.");
        Assert.True(relayTotalMs >= codexMs, "relayTotalMs is below codexMs.");
        Assert.True(execution.GetProperty("requestReadMs").GetDouble() >= 0);
        Assert.True(execution.GetProperty("relayOverheadMs").GetDouble() >= 0);
        return codexMs;
    }

    /// <summary>How many Codex processes that were not there before are still there, after a short wait.</summary>
    private static async Task<int> RemainingCodexAsync(HashSet<int> before)
    {
        for (var attempt = 0; ; attempt++)
        {
            var remaining = LiveCodexRelay.CodexProcessIds().Count(id => !before.Contains(id));
            if (remaining == 0 || attempt == 50)
            {
                return remaining;
            }

            await Task.Delay(100);
        }
    }
}

/// <summary>
/// What the two opt-in live tests share (test plan section 3): the Codex that is installed on this PC,
/// found the way the application finds it, and a real relay on a port the system picks, in a temporary
/// data folder, with the real process runner. Nothing here runs unless a live test was opted into.
/// </summary>
internal sealed class LiveCodexRelay : IAsyncDisposable
{
    /// <summary>The reasoning efforts of protocol C-05, lowest first.</summary>
    private static readonly string[] Efforts = ["none", "minimal", "low", "medium", "high", "xhigh", "max"];

    private readonly IRelay _relay;
    private readonly HttpClient _client;

    private LiveCodexRelay(string codexPath, string? codexVersion, TempFolder data, IRelay relay, HttpClient client)
    {
        CodexPath = codexPath;
        CodexVersion = codexVersion;
        Data = data;
        _relay = relay;
        _client = client;
    }

    public string CodexPath { get; }

    /// <summary>The version text of the Codex in use, or null when it could not be read.</summary>
    public string? CodexVersion { get; }

    /// <summary>The relay's data folder.</summary>
    public TempFolder Data { get; }

    /// <summary>The folder that holds one folder per answer request in flight.</summary>
    public string WorkFolder => Data.Combine("work");

    /// <summary>
    /// Finds Codex with the real locator, requires <c>codex login status</c> to succeed, and starts the
    /// relay with an endpoint on a reserved test name.
    /// </summary>
    public static async Task<LiveCodexRelay> StartAsync()
    {
        var runner = new ProcessRunner(TimeProvider.System);
        var location = new CodexLocator(new HostFileSystem()).Locate(null);
        Assert.True(location.Problem == CodexProblem.None, "Codex was not found on this PC.");
        var codexPath = location.Path!;

        var questions = new CodexSignInCheck(runner);
        Assert.True(
            await questions.IsSignedInAsync(codexPath, CancellationToken.None),
            "codex login status did not succeed: sign in to Codex on this PC first.");
        var version = await questions.ReadVersionAsync(codexPath, CancellationToken.None);

        var data = new TempFolder();
        IRelay? relay = null;
        HttpClient? client = null;
        try
        {
            relay = RelayFactory.Create(
                new RelayOptions(
                    Port: 0,
                    AdvertisedEndpoint: "https://pc.example.test/",
                    Mode: ConnectionMode.Private,
                    DataDirectory: data.Path,
                    CodexExecutablePath: codexPath,
                    LabFolder: null,
                    LabPort: RelaySettings.Default.AsrLabPort,
                    RelayVersion: "1.0.0"),
                new RelayDependencies { Processes = runner });
            await relay.StartAsync(CancellationToken.None);

            // The token never leaves this process: it goes from the relay into the client's header.
            var payload = await ((IPairingExport)relay).GetPairingPayloadAsync(CancellationToken.None);
            using var parsed = JsonDocument.Parse(payload);
            client = new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:" + relay.BoundPort.ToString(CultureInfo.InvariantCulture)),
                Timeout = TimeSpan.FromMinutes(5),
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Authorization", "Bearer " + parsed.RootElement.GetProperty("token").GetString());
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-LiveCue-Protocol", "2");
            return new LiveCodexRelay(codexPath, version, data, relay, client);
        }
        catch
        {
            client?.Dispose();
            if (relay is not null)
            {
                await relay.DisposeAsync();
            }

            data.Dispose();
            throw;
        }
    }

    /// <summary>The ids of every process named "codex" on this PC right now.</summary>
    public static HashSet<int> CodexProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("codex"))
        {
            using (process)
            {
                ids.Add(process.Id);
            }
        }

        return ids;
    }

    /// <summary>The first offered model and its lowest reasoning effort, from <c>GET /v1/models</c>.</summary>
    public async Task<(string Model, string Effort)> FirstSelectionAsync()
    {
        using var response = await _client.GetAsync(new Uri("/v1/models", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var catalogue = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var models = catalogue.RootElement.GetProperty("models");
        Assert.True(models.GetArrayLength() > 0, "The catalogue of this Codex offers no model.");
        var first = models[0];
        Assert.False(string.IsNullOrEmpty(first.GetProperty("name").GetString()));
        var efforts = first.GetProperty("reasoningEfforts").EnumerateArray().Select(effort => effort.GetString()!).ToList();
        Assert.NotEmpty(efforts);
        return (first.GetProperty("id").GetString()!, efforts.MinBy(effort => Array.IndexOf(Efforts, effort))!);
    }

    /// <summary>One answer request with a fresh request id. The body's other members are the caller's.</summary>
    public async Task<(HttpStatusCode Status, string Body)> PostAsync(
        string route, (string Model, string Effort) selection, string transcript)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["requestId"] = Guid.NewGuid().ToString("D").ToUpperInvariant(),
            ["sessionId"] = Guid.NewGuid().ToString("D").ToUpperInvariant(),
            ["assistant"] = new Dictionary<string, string>
            {
                ["model"] = selection.Model,
                ["reasoningEffort"] = selection.Effort,
                ["provider"] = "codex",
            },
            ["memory"] = new Dictionary<string, object>
            {
                ["summary"] = string.Empty,
                ["facts"] = Array.Empty<string>(),
                ["openQuestions"] = Array.Empty<string>(),
            },
            ["transcript"] = transcript,
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(new Uri(route, UriKind.Relative), content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Stops the relay. Afterwards no answer run of it can still be alive.</summary>
    public Task StopAsync() => _relay.StopAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _relay.DisposeAsync();
        Data.Dispose();
    }
}
