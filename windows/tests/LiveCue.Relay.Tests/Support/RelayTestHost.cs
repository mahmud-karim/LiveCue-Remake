using System.Net.WebSockets;
using System.Text.Json;
using LiveCue.Relay.LocalModel;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Support;

/// <summary>What a test may change about the relay under test before it is created.</summary>
internal sealed class RelayTestHostOptions
{
    public ConnectionMode Mode { get; set; } = ConnectionMode.Private;

    /// <summary>The advertised endpoint handed to the relay; a reserved test name.</summary>
    public string Endpoint { get; set; } = RelayTestHost.Endpoint;

    /// <summary>The loopback port to ask for. 0, the default, lets the system pick a free one.</summary>
    public int Port { get; set; }

    /// <summary>
    /// The Codex path in the options. It is never started (the answer backend and the catalogue source
    /// are fakes); it only says "Codex was found". Null says none was found.
    /// </summary>
    public string? CodexExecutablePath { get; set; } = RelayTestHost.FakeCodexPath;

    /// <summary>True, the default, hands the relay a lab folder; false means "PC speech is not configured".</summary>
    public bool LabConfigured { get; set; } = true;

    /// <summary>False, the default, so that the catalogue's call counter starts at zero.</summary>
    public bool WarmCatalogOnStart { get; set; }

    /// <summary>
    /// Replaces the whole PC model controller (the "prepare stub" of protocol 15.6). Tests of the speech
    /// bridge set it; everywhere else the real controller runs over <see cref="RelayTestHost.Lab"/>.
    /// </summary>
    public ILocalModelController? LocalModelController { get; set; }

    /// <summary>
    /// The real clock instead of the manual one, for the few tests that wait for a real Kestrel timer.
    /// </summary>
    public bool UseSystemClock { get; set; }
}

/// <summary>
/// A real <see cref="IRelay"/> on a real Kestrel listener, on a port the system picks (port 0), with
/// every seam faked, the fixture catalogue of protocol section 15, a temporary data folder, and the
/// accepted token read through <see cref="IPairingExport"/>. Every test gets its own.
/// </summary>
internal sealed class RelayTestHost : IAsyncDisposable
{
    public const string Endpoint = "https://pc.example.test:11443/";

    public const string RelayVersion = "1.0.0";

    /// <summary>A path that looks like a Codex installation. Nothing exists there and nothing is started.</summary>
    public const string FakeCodexPath = @"C:\LiveCue-tests\codex\codex.exe";

    private RelayTestHost(RelayTestHostOptions options)
    {
        Clock = new ManualTimeProvider();
        Data = new TempFolder();
        Temp = new TempFolder();
        LocalModelController = options.LocalModelController;

        Options = new RelayOptions(
            Port: options.Port,
            AdvertisedEndpoint: options.Endpoint,
            Mode: options.Mode,
            DataDirectory: Data.Path,
            CodexExecutablePath: options.CodexExecutablePath,
            LabFolder: options.LabConfigured ? Temp.Combine("lab") : null,
            LabPort: 47842,
            RelayVersion: RelayVersion,
            WarmCatalogOnStart: options.WarmCatalogOnStart);

        Dependencies = new RelayDependencies
        {
            Clock = options.UseSystemClock ? TimeProvider.System : Clock,
            Processes = Processes,
            TempRoot = Temp.Path,
            PairingProtector = Protector,
            PairingFiles = PairingFiles,
            SelfCheckClient = SelfCheck,
            CatalogSource = Catalog,
            AnswerBackend = Backend,
            LabRuntime = Lab,
            LabSocketConnector = LabSockets,
            LocalModelController = options.LocalModelController,
        };

        Relay = RelayFactory.Create(Options, Dependencies);
        Events = new EventCollector(Relay.Events);
    }

    public IRelay Relay { get; }

    public RelayOptions Options { get; }

    public RelayDependencies Dependencies { get; }

    /// <summary>The relay's clock, unless the test asked for the system clock.</summary>
    public ManualTimeProvider Clock { get; }

    /// <summary>The data folder: pairing.json (through <see cref="PairingFiles"/>) and schemas\.</summary>
    public TempFolder Data { get; }

    /// <summary>The parent of the per-request Codex folders, and of the pretend lab folder.</summary>
    public TempFolder Temp { get; }

    public FakeAnswerBackend Backend { get; } = new();

    public FakeCatalogSource Catalog { get; } = new();

    public FakeLabRuntime Lab { get; } = new();

    public FakeLabSocketConnector LabSockets { get; } = new();

    public FakePairingProtector Protector { get; } = new();

    public InMemoryPairingFiles PairingFiles { get; } = new();

    public FakeSelfCheckClient SelfCheck { get; } = new();

    public FakeProcessRunner Processes { get; } = new();

    /// <summary>The controller the test supplied, or null when the real one runs.</summary>
    public ILocalModelController? LocalModelController { get; }

    /// <summary>Every event of the relay. The collector is the channel's single reader.</summary>
    public EventCollector Events { get; }

    /// <summary>The loopback port, once started.</summary>
    public int Port { get; private set; }

    /// <summary>The token the relay accepts, once started. After a reset, call <see cref="ReadTokenAsync"/>.</summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>A client pointed at the relay. It adds nothing to a request by itself.</summary>
    public HttpClient Client { get; private set; } = new();

    /// <summary>A relay that has been started and whose token is known.</summary>
    public static async Task<RelayTestHost> StartAsync(Action<RelayTestHostOptions>? configure = null)
    {
        var host = Create(configure);
        try
        {
            await host.StartRelayAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>A relay that has not been started, for tests of the start itself.</summary>
    public static RelayTestHost Create(Action<RelayTestHostOptions>? configure = null)
    {
        var options = new RelayTestHostOptions();
        configure?.Invoke(options);
        return new RelayTestHost(options);
    }

    /// <summary>Starts the relay, reads the accepted token and points the client at the bound port.</summary>
    public async Task StartRelayAsync()
    {
        await Relay.StartAsync(CancellationToken.None);
        Port = Relay.BoundPort;
        Client.Dispose();
        Client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{Port}"),
        };
        await ReadTokenAsync();
    }

    /// <summary>Reads the accepted token again through <see cref="IPairingExport"/> (after a reset).</summary>
    public async Task<string> ReadTokenAsync()
    {
        var payload = await ((IPairingExport)Relay).GetPairingPayloadAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(payload);
        Token = document.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("The pairing payload has no token.");
        return Token;
    }

    /// <summary>
    /// A request as a correct phone sends it: the accepted token and protocol 2. Pass null to leave a
    /// header out, or another value to send that instead.
    /// </summary>
    /// <param name="authorization">The whole Authorization value. The default is "Bearer " + the accepted token.</param>
    public HttpRequestMessage Request(
        HttpMethod method,
        string target,
        string? authorization = UseAcceptedToken,
        string? protocol = "2",
        string? json = null)
    {
        var request = new HttpRequestMessage(method, target);
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "Authorization", authorization == UseAcceptedToken ? TestTokens.Bearer(Token) : authorization);
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

    /// <summary>Marks "send the accepted token" in <see cref="Request"/>. Not a token.</summary>
    public const string UseAcceptedToken = "\u0001accepted";

    public Task<HttpResponseMessage> GetAsync(string target) => SendAsync(Request(HttpMethod.Get, target));

    public Task<HttpResponseMessage> PostAsync(string target, string json) =>
        SendAsync(Request(HttpMethod.Post, target, json: json));

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await Client.SendAsync(request);
        }
    }

    /// <summary>A raw connection to the relay, for requests no client library would send.</summary>
    public Task<RawHttp> OpenRawAsync() => RawHttp.ConnectAsync(Port);

    /// <summary>The head of a raw request with the accepted token and protocol 2, then any extra headers.</summary>
    public string RawHead(string requestLine, params (string Name, string Value)[] extraHeaders) => RawHttp.Head(
        requestLine,
        Port,
        [("Authorization", TestTokens.Bearer(Token)), ("X-LiveCue-Protocol", "2"), .. extraHeaders]);

    /// <summary>The speech socket's address on loopback.</summary>
    public Uri SpeechUri => new($"ws://127.0.0.1:{Port}/v1/speech");

    /// <summary>
    /// A socket prepared as a correct phone opens it (protocol 10.1). Pass null to leave a header out.
    /// The caller connects it to <see cref="SpeechUri"/>.
    /// </summary>
    public ClientWebSocket CreateSpeechSocket(
        string? model = "nemotron",
        string? authorization = UseAcceptedToken,
        string? protocol = "2")
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        if (authorization is not null)
        {
            socket.Options.SetRequestHeader(
                "Authorization", authorization == UseAcceptedToken ? TestTokens.Bearer(Token) : authorization);
        }

        if (protocol is not null)
        {
            socket.Options.SetRequestHeader("X-LiveCue-Protocol", protocol);
        }

        if (model is not null)
        {
            socket.Options.SetRequestHeader("X-LiveCue-Speech-Model", model);
        }

        return socket;
    }

    /// <summary>Opens a speech socket as a correct phone does.</summary>
    public async Task<ClientWebSocket> OpenSpeechAsync(string? model = "nemotron")
    {
        var socket = CreateSpeechSocket(model);
        try
        {
            await socket.ConnectAsync(SpeechUri, CancellationToken.None);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Relay.DisposeAsync();
        Data.Dispose();
        Temp.Dispose();
    }
}
