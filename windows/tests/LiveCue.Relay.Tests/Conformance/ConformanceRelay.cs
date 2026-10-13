using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Speech;
using LiveCue.Relay.Tests.Processes;
using LiveCue.Relay.Tests.Speech;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>What a conformance test may change about the relay before it is created.</summary>
internal sealed class ConformanceOptions
{
    public ConnectionMode Mode { get; set; } = ConnectionMode.Private;

    /// <summary>False: no lab folder is configured ("PC speech is not set up").</summary>
    public bool LabConfigured { get; set; } = true;

    /// <summary>False: no Codex was found on the PC.</summary>
    public bool CodexFound { get; set; } = true;

    /// <summary>
    /// A manual clock for the relay's own limits. Null, the default, is the system clock: with real child
    /// processes behind the relay, real time is what production runs on.
    /// </summary>
    public ManualTimeProvider? Clock { get; set; }

    /// <summary>A data folder that is already there (a second start on the folder of a first).</summary>
    public string? DataDirectory { get; set; }

    public bool WarmCatalogOnStart { get; set; }

    /// <summary>Replaces the fake lab runtime (<see cref="ConformanceRelay.Lab"/> is then unused).</summary>
    public ILabRuntime? LabRuntime { get; set; }

    /// <summary>Replaces the fake lab socket connector (<see cref="ConformanceRelay.LabSockets"/> is then unused).</summary>
    public ILabSocketConnector? LabSocketConnector { get; set; }
}

/// <summary>
/// The cross-area harness of test plan 1.7: one real <see cref="IRelay"/> on a real Kestrel listener on a
/// port the system picks, with every relay area the production one:
/// <list type="bullet">
/// <item>the real edge and routes;</item>
/// <item>the real pairing service over real files in a temporary data folder, protected by real DPAPI;</item>
/// <item>the real catalogue source and the real answer backend, through the real process runner, with
/// <c>LiveCue.TestChild</c> standing in for the Codex executable (nothing else is ever started);</item>
/// <item>the real PC model controller over the fake lab runtime;</item>
/// <item>the real speech bridge over the in-process fake lab socket.</item>
/// </list>
/// The self-check client is the fake: the real one would dial the advertised endpoint. The per-request
/// Codex folders live under the data folder (<c>work\</c>), as in production.
/// </summary>
internal sealed class ConformanceRelay : IAsyncDisposable
{
    /// <summary>The one model the stand-in app-server offers, and its two efforts.</summary>
    public const string Model = "gpt-stand-in";

    public const string Effort = "low";

    public const string Transcript = "Fixture question";

    private readonly TempFolder? _ownData;
    private readonly TempFolder _other = new();

    private ConformanceRelay(ConformanceOptions options)
    {
        _ownData = options.DataDirectory is null ? new TempFolder() : null;
        DataDirectory = options.DataDirectory ?? _ownData!.Path;
        ManualClock = options.Clock;
        TimeProvider clock = options.Clock is null ? TimeProvider.System : options.Clock;
        Codex = new ScriptedCodexRunner(new ProcessRunner(clock), _other.Combine("reports"));

        Options = new RelayOptions(
            Port: 0,
            AdvertisedEndpoint: RelayTestHost.Endpoint,
            Mode: options.Mode,
            DataDirectory: DataDirectory,
            CodexExecutablePath: options.CodexFound ? RelayTestHost.FakeCodexPath : null,
            LabFolder: options.LabConfigured ? _other.Combine("asr") : null,
            LabPort: 47842,
            RelayVersion: RelayTestHost.RelayVersion,
            WarmCatalogOnStart: options.WarmCatalogOnStart);

        Relay = RelayFactory.Create(Options, new RelayDependencies
        {
            Clock = clock,
            Processes = Codex,
            SelfCheckClient = SelfCheck,
            LabRuntime = options.LabRuntime ?? Lab,
            LabSocketConnector = options.LabSocketConnector ?? LabSockets,
        });
        Events = new EventCollector(Relay.Events);
    }

    public IRelay Relay { get; }

    public RelayOptions Options { get; }

    public string DataDirectory { get; }

    /// <summary>The manual clock, when the test asked for one.</summary>
    public ManualTimeProvider? ManualClock { get; }

    public ScriptedCodexRunner Codex { get; }

    public FakeLabRuntime Lab { get; } = new();

    public FakeLabSocketConnector LabSockets { get; } = new();

    public FakeSelfCheckClient SelfCheck { get; } = new();

    public EventCollector Events { get; }

    public int Port { get; private set; }

    public string Token { get; private set; } = string.Empty;

    public HttpClient Client { get; private set; } = new();

    /// <summary>The edge of the running relay: its gauge, its connection count and its counters.</summary>
    public EdgePipeline Edge => ((RelayHost)Relay).Edge ?? throw new InvalidOperationException("The relay is not running.");

    public static async Task<ConformanceRelay> StartAsync(Action<ConformanceOptions>? configure = null)
    {
        var options = new ConformanceOptions();
        configure?.Invoke(options);
        var relay = new ConformanceRelay(options);
        try
        {
            await relay.Relay.StartAsync(CancellationToken.None);
            relay.Port = relay.Relay.BoundPort;
            relay.Client.Dispose();
            relay.Client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{relay.Port}"),
                Timeout = TimeSpan.FromSeconds(90),
            };
            await relay.ReadTokenAsync();
            return relay;
        }
        catch
        {
            await relay.DisposeAsync();
            throw;
        }
    }

    /// <summary>Reads the accepted token again through <see cref="IPairingExport"/> (after a reset).</summary>
    public async Task<string> ReadTokenAsync()
    {
        var payload = await ((IPairingExport)Relay).GetPairingPayloadAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(payload);
        Token = document.RootElement.GetProperty("token").GetString()!;
        return Token;
    }

    /// <summary>An assist or summary body with a selection the stand-in catalogue offers.</summary>
    public static string AnswerBody(string requestId = Bodies.RequestId, string transcript = Transcript) =>
        $$"""{"requestId":"{{requestId}}","assistant":{"model":"{{Model}}","reasoningEffort":"{{Effort}}"},"transcript":"{{transcript}}"}""";

    /// <summary>A request as a correct phone sends it. Pass null to leave a header out.</summary>
    public HttpRequestMessage Request(
        HttpMethod method,
        string target,
        string? json = null,
        string? token = "",
        string? protocol = "2")
    {
        var request = new HttpRequestMessage(method, target);
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", TestTokens.Bearer(token.Length == 0 ? Token : token));
        }

        if (protocol is not null)
        {
            request.Headers.TryAddWithoutValidation("X-LiveCue-Protocol", protocol);
        }

        if (json is not null)
        {
            request.Content = Bodies.Json(json);
        }

        return request;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await Client.SendAsync(request);
        }
    }

    public Task<HttpResponseMessage> GetAsync(string target) => SendAsync(Request(HttpMethod.Get, target));

    public Task<HttpResponseMessage> PostAsync(string target, string json) => SendAsync(Request(HttpMethod.Post, target, json));

    public Task<HttpResponseMessage> DeleteAsync(string target) => SendAsync(Request(HttpMethod.Delete, target));

    /// <summary>The status and the body text of one request.</summary>
    public async Task<(int Status, string Body)> ExchangeAsync(HttpRequestMessage request)
    {
        using var response = await SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public Task<(int Status, string Body)> ExchangeAsync(HttpMethod method, string target, string? json = null) =>
        ExchangeAsync(Request(method, target, json));

    /// <summary>The request a row of the route table is served for, with valid input.</summary>
    public HttpRequestMessage RequestFor(RouteEntry route) => route.Id switch
    {
        RouteId.Health or RouteId.Models or RouteId.LocalModelStatus => Request(HttpMethod.Get, route.Target),
        RouteId.PairVerify => Request(HttpMethod.Post, route.Target, "{}"),
        RouteId.LocalModelStart or RouteId.LocalModelStop => Request(HttpMethod.Post, route.Target, Bodies.LocalModel("nemotron")),
        RouteId.Assist or RouteId.SessionSummary => Request(HttpMethod.Post, route.Target, AnswerBody()),
        RouteId.CancelRequest => Request(HttpMethod.Delete, route.Target + Bodies.RequestId),
        _ => throw new ArgumentOutOfRangeException(nameof(route), "The speech route is not an HTTP request."),
    };

    public Task<RawHttp> OpenRawAsync() => RawHttp.ConnectAsync(Port);

    /// <summary>The head of a raw request with the accepted token and protocol 2, then any extra headers.</summary>
    public string RawHead(string requestLine, params (string Name, string Value)[] extraHeaders) => RawHttp.Head(
        requestLine,
        Port,
        [("Authorization", TestTokens.Bearer(Token)), ("X-LiveCue-Protocol", "2"), .. extraHeaders]);

    /// <summary>A raw request with a body and its truthful length.</summary>
    public string RawPost(string target, string json, params (string Name, string Value)[] extraHeaders) =>
        RawHead(
            $"POST {target} HTTP/1.1",
            [("Content-Length", Encoding.UTF8.GetByteCount(json).ToString(System.Globalization.CultureInfo.InvariantCulture)), .. extraHeaders])
        + json;

    public Uri SpeechUri => new($"ws://127.0.0.1:{Port}/v1/speech");

    /// <summary>Opens a speech socket as a correct phone does (protocol 10.1).</summary>
    public async Task<SpeechPhone> OpenSpeechAsync(string? model = "nemotron", string? token = "", string? protocol = "2")
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        if (token is not null)
        {
            socket.Options.SetRequestHeader("Authorization", TestTokens.Bearer(token.Length == 0 ? Token : token));
        }

        if (protocol is not null)
        {
            socket.Options.SetRequestHeader("X-LiveCue-Protocol", protocol);
        }

        if (model is not null)
        {
            socket.Options.SetRequestHeader("X-LiveCue-Speech-Model", model);
        }

        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(SpeechUri, limit.Token);
            return new SpeechPhone(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A stream that is admitted and past <c>ready</c>: the lab runtime reports the model as loaded, so
    /// that the real controller's prepare succeeds without a start.
    /// </summary>
    public async Task<SpeechPhone> OpenReadyStreamAsync(string model = "nemotron")
    {
        Lab.LoadedModel = model;
        LabSockets.Model = model;
        var phone = await OpenSpeechAsync(model);
        try
        {
            await phone.WaitForAsync("ready");
            return phone;
        }
        catch
        {
            phone.Dispose();
            throw;
        }
    }

    /// <summary>Every file under the data folder, relative, sorted.</summary>
    public IReadOnlyList<string> DataFiles() =>
    [
        .. Directory.EnumerateFiles(DataDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(DataDirectory, file))
            .Order(StringComparer.Ordinal),
    ];

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Relay.DisposeAsync();
        _ownData?.Dispose();
        _other.Dispose();
    }
}

/// <summary>What the stand-in Codex process reported about one <c>exec</c> run.</summary>
internal sealed record CodexRunReport(
    int ProcessId,
    string WorkingFolder,
    string Prompt,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> EnvironmentNames);

/// <summary>
/// The real <see cref="ProcessRunner"/> with <c>LiveCue.TestChild</c> standing in for Codex, as
/// <c>StandInCodexRunner</c> does, but with a scenario a test can change between requests and one report
/// file per run. A request for anything that is not the Codex stand-in (a lab script, for one) is refused:
/// in this harness nothing but the test child is ever started.
/// </summary>
internal sealed class ScriptedCodexRunner : IProcessRunner
{
    private readonly ProcessRunner _real;
    private readonly string _reports;
    private readonly Lock _gate = new();
    private readonly List<ProcessRequest> _requests = [];
    private readonly List<string> _execReports = [];
    private int _next;

    public ScriptedCodexRunner(ProcessRunner real, string reportFolder)
    {
        _real = real;
        _reports = reportFolder;
        Directory.CreateDirectory(reportFolder);
    }

    /// <summary>What <c>codex exec</c> plays next: ok, fail, no-output, big or hang.</summary>
    public string ExecScenario { get; set; } = "ok";

    /// <summary>What <c>codex app-server</c> plays next: ok, early-exit, silent, flood or error.</summary>
    public string CatalogScenario { get; set; } = "ok";

    /// <summary>
    /// When it returns an exception, the runner throws it instead of starting anything: a collaborator
    /// that breaks in a way no area expects.
    /// </summary>
    public Func<ProcessRequest, Exception?>? Fault { get; set; }

    /// <summary>Every request as the relay made it, in order.</summary>
    public IReadOnlyList<ProcessRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public IReadOnlyList<ProcessRequest> ExecRequests => [.. Requests.Where(request => IsMode(request, "exec"))];

    public IReadOnlyList<ProcessRequest> CatalogRequests => [.. Requests.Where(request => IsMode(request, "app-server"))];

    /// <summary>The report file of each exec run, in order; a run writes it once it has read its prompt.</summary>
    public IReadOnlyList<string> ExecReportFiles
    {
        get
        {
            lock (_gate)
            {
                return [.. _execReports];
            }
        }
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct) =>
        _real.RunAsync(Redirect(request), ct);

    public Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct) =>
        _real.StartSessionAsync(Redirect(request), ct);

    /// <summary>Waits until the exec run of that index has written its report, and reads it.</summary>
    public async Task<CodexRunReport> ReadExecReportAsync(int index = 0)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var files = ExecReportFiles;
            if (index < files.Count && File.Exists(files[index]))
            {
                try
                {
                    using var document = JsonDocument.Parse(TestChild.ReadShared(files[index]));
                    var root = document.RootElement;
                    return new CodexRunReport(
                        root.GetProperty("processId").GetInt32(),
                        root.GetProperty("workingFolder").GetString()!,
                        root.GetProperty("prompt").GetString()!,
                        [.. root.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()!)],
                        [.. root.GetProperty("environment").EnumerateArray().Select(item => item.GetString()!)]);
                }
                catch (Exception exception) when (exception is JsonException or IOException)
                {
                    // The child is writing the file right now.
                }
            }

            Assert.True(DateTime.UtcNow < until, "The Codex stand-in did not write its report in time.");
            await Task.Delay(20);
        }
    }

    private static bool IsMode(ProcessRequest request, string mode) =>
        request.Arguments.Count > 0 && string.Equals(request.Arguments[0], mode, StringComparison.Ordinal);

    private ProcessRequest Redirect(ProcessRequest request)
    {
        string scenario;
        string report;
        lock (_gate)
        {
            _requests.Add(request);
            if (Fault?.Invoke(request) is { } failure)
            {
                throw failure;
            }

            report = Path.Combine(_reports, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"run-{_next++}.json"));
            if (IsMode(request, "exec"))
            {
                scenario = ExecScenario;
                _execReports.Add(report);
            }
            else if (IsMode(request, "app-server"))
            {
                scenario = CatalogScenario;
            }
            else
            {
                throw new InvalidOperationException("The conformance harness starts nothing but the Codex stand-in.");
            }
        }

        if (!string.Equals(request.FileName, RelayTestHost.FakeCodexPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The conformance harness starts nothing but the Codex stand-in.");
        }

        return request with
        {
            FileName = DotnetHost.Path,
            Arguments = [TestChild.Dll, .. request.Arguments, "--stub", scenario, "--report", report],
        };
    }
}
