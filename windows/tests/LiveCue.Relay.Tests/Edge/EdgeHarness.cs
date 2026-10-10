using System.Text.Json;
using LiveCue.Relay.Core;
using LiveCue.Relay.Diagnostics;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Routes;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;
using Microsoft.AspNetCore.Builder;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>A route handler that a test writes: it hangs, throws or answers as the test says.</summary>
internal sealed class ScriptedRoute(Func<RouteContext, Task> handle) : IRouteHandler
{
    private int _calls;

    /// <summary>How often the edge called the handler.</summary>
    public int Calls => Volatile.Read(ref _calls);

    public Task HandleAsync(RouteContext context)
    {
        Interlocked.Increment(ref _calls);
        return handle(context);
    }
}

/// <summary>
/// The real edge on a real Kestrel listener (port 0), built exactly as the relay host builds it, with
/// one difference: a test can put its own handler behind any route. That is what proves the rows of
/// W-design 4.4 that depend on what a handler does (it throws, it outlasts its deadline, it never
/// answers) without depending on another area's behaviour. Everything before the handler is the
/// production code: the listener, the connection gate, the pipeline, the body reader and the response
/// writer.
/// </summary>
internal sealed class EdgeHarness : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly TempFolder _data;
    private readonly TempFolder _temp;

    private EdgeHarness(
        WebApplication app,
        TempFolder data,
        TempFolder temp,
        ManualTimeProvider clock,
        EventChannel channel,
        AccessState access,
        RelayServices services,
        EdgePipeline pipeline,
        int port,
        string token)
    {
        _app = app;
        _data = data;
        _temp = temp;
        Clock = clock;
        Channel = channel;
        Access = access;
        Services = services;
        Pipeline = pipeline;
        Port = port;
        Token = token;
        Events = new EventCollector(channel.Reader);
        Client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
    }

    public ManualTimeProvider Clock { get; }

    public EventChannel Channel { get; }

    public EventCollector Events { get; }

    public AccessState Access { get; }

    public RelayServices Services { get; }

    public EdgePipeline Pipeline { get; }

    public int Port { get; }

    /// <summary>The token the edge accepts.</summary>
    public string Token { get; }

    public HttpClient Client { get; }

    /// <summary>The services of the listener's application, for the test that reads its effective limits.</summary>
    public IServiceProvider ApplicationServices => _app.Services;

    /// <param name="routes">Replaces handlers of the composed relay; the others stay the real ones.</param>
    public static async Task<EdgeHarness> StartAsync(
        Action<Dictionary<RouteId, IRouteHandler>>? routes = null,
        ConnectionMode mode = ConnectionMode.Private)
    {
        var clock = new ManualTimeProvider();
        var data = new TempFolder();
        var temp = new TempFolder();
        var options = new RelayOptions(
            Port: 0,
            AdvertisedEndpoint: RelayTestHost.Endpoint,
            Mode: mode,
            DataDirectory: data.Path,
            CodexExecutablePath: RelayTestHost.FakeCodexPath,
            LabFolder: null,
            LabPort: 47842,
            RelayVersion: RelayTestHost.RelayVersion,
            WarmCatalogOnStart: false);
        var dependencies = new RelayDependencies
        {
            Clock = clock,
            Processes = new FakeProcessRunner(),
            TempRoot = temp.Path,
            PairingProtector = new FakePairingProtector(),
            PairingFiles = new InMemoryPairingFiles(),
            SelfCheckClient = new FakeSelfCheckClient(),
            CatalogSource = new FakeCatalogSource(),
            AnswerBackend = new FakeAnswerBackend(),
            LabRuntime = new FakeLabRuntime(),
            LabSocketConnector = new FakeLabSocketConnector(),
        };

        var channel = new EventChannel(clock);
        var access = new AccessState();
        Assert.True(AdvertisedEndpoint.TryCanonicalize(options.AdvertisedEndpoint, out var endpoint));
        var context = new RelayContext(options, dependencies, channel, access, dependencies.Processes, endpoint);
        var composed = RelayComposition.Compose(context);
        await composed.Pairing.LoadOrCreateAsync(CancellationToken.None);

        var handlers = new Dictionary<RouteId, IRouteHandler>(composed.Routes);
        routes?.Invoke(handlers);
        var services = new RelayServices
        {
            Pairing = composed.Pairing,
            Catalog = composed.Catalog,
            Answers = composed.Answers,
            LocalModel = composed.LocalModel,
            Speech = composed.Speech,
            Lease = composed.Lease,
            Routes = handlers,
        };

        var pipeline = new EdgePipeline(context, services);
        var app = KestrelSetup.Build(context, pipeline);
        await app.StartAsync();

        using var payload = JsonDocument.Parse(await composed.Pairing.GetPayloadAsync(CancellationToken.None));
        var token = payload.RootElement.GetProperty("token").GetString()!;
        return new EdgeHarness(
            app, data, temp, clock, channel, access, services, pipeline, KestrelSetup.ReadBoundPort(app), token);
    }

    /// <summary>A request as a correct phone sends it; null leaves a header out.</summary>
    public HttpRequestMessage Request(HttpMethod method, string target, string? json = null, string? protocol = "2")
    {
        var request = new HttpRequestMessage(method, target);
        request.Headers.TryAddWithoutValidation("Authorization", TestTokens.Bearer(Token));
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

    /// <summary>One request to a row of the route table, with the body a body route needs.</summary>
    public HttpRequestMessage RequestFor(RouteEntry route) => Request(
        new HttpMethod(route.Method),
        route.Id == RouteId.CancelRequest ? route.Target + Bodies.RequestId : route.Target,
        route.ReadsBody ? "{}" : null);

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await Client.SendAsync(request);
        }
    }

    public Task<RawHttp> OpenRawAsync() => RawHttp.ConnectAsync(Port);

    /// <summary>The head of a raw request with the accepted token and protocol 2, then any extra headers.</summary>
    public string RawHead(string requestLine, params (string Name, string Value)[] extraHeaders) => RawHttp.Head(
        requestLine,
        Port,
        [("Authorization", TestTokens.Bearer(Token)), ("X-LiveCue-Protocol", "2"), .. extraHeaders]);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Access.BeginStopping();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _app.StopAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            // What a test left open is aborted with the listener.
        }

        await _app.DisposeAsync();
        Channel.Complete();
        Access.Dispose();
        _data.Dispose();
        _temp.Dispose();
    }
}
