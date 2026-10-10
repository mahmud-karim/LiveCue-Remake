using LiveCue.Relay.Pairing;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests;

/// <summary>
/// A real relay on a real Kestrel listener, on a port the system picks (port 0), with an HTTP client
/// pointed at it and a known pairing token injected through the test seam. Every test gets its own.
/// </summary>
internal sealed class RelayTestHost : IAsyncDisposable
{
    /// <summary>The accepted token: well formed (43 base64url characters) and known to the tests.</summary>
    public static readonly string Token = new('t', ProtocolConstants.TokenLength);

    /// <summary>A token of the right shape that the relay does not accept.</summary>
    public static readonly string OtherToken = new('x', ProtocolConstants.TokenLength);

    public const string Endpoint = "https://pc.example.test:11443/";

    private readonly List<RelayEvent> _events = [];

    private RelayTestHost(IRelay relay)
    {
        Relay = relay;
    }

    public IRelay Relay { get; }

    public HttpClient Client { get; private set; } = null!;

    public int Port { get; private set; }

    /// <summary>Every event raised so far, in order.</summary>
    public IReadOnlyList<RelayEvent> Events
    {
        get
        {
            while (Relay.Events.TryRead(out var relayEvent))
            {
                _events.Add(relayEvent);
            }

            return [.. _events];
        }
    }

    public static async Task<RelayTestHost> StartAsync(
        ConnectionMode mode = ConnectionMode.Private,
        TimeProvider? clock = null)
    {
        var host = new RelayTestHost(Create(Options(mode: mode), clock));
        await host.Relay.StartAsync(CancellationToken.None);
        host.Port = host.Relay.BoundPort;
        host.Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.Port}") };
        return host;
    }

    public static IRelay Create(RelayOptions options, TimeProvider? clock = null) =>
        RelayFactory.Create(
            options,
            new RelayDependencies
            {
                Clock = clock ?? TimeProvider.System,
                TokenVerifier = HashedTokenVerifier.For(Token),
            });

    public static RelayOptions Options(
        int port = 0,
        string endpoint = Endpoint,
        ConnectionMode mode = ConnectionMode.Private) =>
        new(
            Port: port,
            AdvertisedEndpoint: endpoint,
            Mode: mode,
            DataDirectory: Path.Combine(Path.GetTempPath(), "LiveCue-relay-tests-not-used"),
            CodexExecutablePath: null,
            LabFolder: null,
            LabPort: 47842,
            RelayVersion: ProtocolConstants.RelayVersion);

    /// <summary>A request as a correct phone sends it: the accepted token and protocol 2.</summary>
    public static HttpRequestMessage Request(
        HttpMethod method,
        string target,
        string? authorization = "",
        string? protocol = "2")
    {
        var request = new HttpRequestMessage(method, target);
        authorization = authorization is "" ? "Bearer " + Token : authorization;
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        if (protocol is not null)
        {
            request.Headers.TryAddWithoutValidation("X-LiveCue-Protocol", protocol);
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await Relay.DisposeAsync();
    }
}
