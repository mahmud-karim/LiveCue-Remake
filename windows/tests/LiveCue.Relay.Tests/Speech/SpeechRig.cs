using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using LiveCue.Relay.Speech;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// What every speech test stands on: a real relay on a real loopback listener (port 0), the manual
/// clock, the "prepare stub" of protocol 15.6 in place of the PC model controller, and the fake lab
/// socket. A test that needs another lab connector, or the real one, says so.
/// </summary>
internal sealed class SpeechRig : IAsyncDisposable
{
    private readonly RelayTestHost? _host;
    private readonly IRelay _relay;
    private readonly TempFolder? _data;
    private readonly TempFolder? _temp;
    private readonly int _port;
    private readonly string _token;

    private SpeechRig(RelayTestHost host, FakeLocalModelController model)
    {
        _host = host;
        _relay = host.Relay;
        _port = host.Port;
        _token = host.Token;
        Model = model;
        Clock = host.Clock;
        Lab = host.LabSockets;
    }

    private SpeechRig(
        IRelay relay,
        int port,
        string token,
        FakeLocalModelController model,
        ManualTimeProvider clock,
        TempFolder data,
        TempFolder temp)
    {
        _relay = relay;
        _port = port;
        _token = token;
        _data = data;
        _temp = temp;
        Model = model;
        Clock = clock;
        Lab = new FakeLabSocketConnector();
    }

    /// <summary>The test host, for its events, its raw connections and its token.</summary>
    public RelayTestHost Host => _host ?? throw new InvalidOperationException("This rig has its own relay.");

    public IRelay Relay => _relay;

    public ManualTimeProvider Clock { get; }

    /// <summary>The prepare stub.</summary>
    public FakeLocalModelController Model { get; }

    /// <summary>The fake lab socket connector (unused by a rig that was given another connector).</summary>
    public FakeLabSocketConnector Lab { get; }

    public EventCollector Events => Host.Events;

    public int Port => _port;

    /// <summary>The usual rig: the test host with the prepare stub and the fake lab socket.</summary>
    public static async Task<SpeechRig> StartAsync(Action<FakeLocalModelController>? configure = null)
    {
        var model = new FakeLocalModelController();
        configure?.Invoke(model);
        var host = await RelayTestHost.StartAsync(options => options.LocalModelController = model);
        return new SpeechRig(host, model);
    }

    /// <summary>
    /// A relay of its own with the given lab connector, or with the production connector dialling
    /// <paramref name="labPort"/> when <paramref name="connector"/> is null. The test host cannot do
    /// this: its connector is always the fake.
    /// </summary>
    public static async Task<SpeechRig> StartWithConnectorAsync(ILabSocketConnector? connector, int labPort = 47842)
    {
        var model = new FakeLocalModelController();
        var clock = new ManualTimeProvider();
        var data = new TempFolder();
        var temp = new TempFolder();
        var options = new RelayOptions(
            Port: 0,
            AdvertisedEndpoint: RelayTestHost.Endpoint,
            Mode: ConnectionMode.Private,
            DataDirectory: data.Path,
            CodexExecutablePath: null,
            LabFolder: temp.Combine("lab"),
            LabPort: labPort,
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
            LabSocketConnector = connector,
            LocalModelController = model,
        };

        var relay = RelayFactory.Create(options, dependencies);
        try
        {
            await relay.StartAsync(CancellationToken.None);
            var payload = await ((IPairingExport)relay).GetPairingPayloadAsync(CancellationToken.None);
            using var document = JsonDocument.Parse(payload);
            var token = document.RootElement.GetProperty("token").GetString()!;
            return new SpeechRig(relay, relay.BoundPort, token, model, clock, data, temp);
        }
        catch
        {
            await relay.DisposeAsync();
            data.Dispose();
            temp.Dispose();
            throw;
        }
    }

    /// <summary>A socket prepared as a correct phone opens it. Pass null to leave a header out.</summary>
    public ClientWebSocket CreateSocket(string? model = "nemotron", string? protocol = "2")
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        socket.Options.Proxy = null;
        socket.Options.SetRequestHeader("Authorization", TestTokens.Bearer(_token));
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

    /// <summary>Opens a speech socket. The upgrade must be accepted.</summary>
    public async Task<SpeechPhone> OpenAsync(string? model = "nemotron", string? protocol = "2")
    {
        var socket = CreateSocket(model, protocol);
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/v1/speech"), limit.Token);
            return new SpeechPhone(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Opens a stream and reads up to its <c>ready</c> frame.</summary>
    public async Task<SpeechPhone> OpenReadyAsync(string model = "nemotron")
    {
        var phone = await OpenAsync(model);
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

    /// <summary>
    /// Opens a stream that is admitted: its first frame, <c>loading</c>, has been read. While the lease
    /// or the place of an earlier stream is still on its way back, the attempt is refused with
    /// SPEECH_BUSY or with HTTP 429; it is then repeated, for a short real time.
    /// </summary>
    public async Task<SpeechPhone> OpenAdmittedAsync(string model = "nemotron")
    {
        var patience = Stopwatch.StartNew();
        while (true)
        {
            Assert.True(patience.Elapsed < TimeSpan.FromSeconds(10), "The speech lease was not released.");
            SpeechPhone phone;
            try
            {
                phone = await OpenAsync(model);
            }
            catch (WebSocketException)
            {
                await Task.Delay(10);
                continue;
            }

            var first = await phone.ReceiveTextAsync();
            if (first is not null && SpeechPhone.TypeOf(SpeechPhone.Parse(first)) == "loading")
            {
                return phone;
            }

            phone.Dispose();
            Assert.True(
                first is not null && first.Contains("SPEECH_BUSY", StringComparison.Ordinal),
                $"The stream was neither admitted nor refused as busy: {first}");
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// One attempt that is refused: its one error code and its close code, as "CODE 1008".
    /// </summary>
    public async Task<string> AttemptAsync(string? model = "nemotron")
    {
        using var phone = await OpenAsync(model);
        var ending = await phone.ReadToEndAsync();
        var error = Assert.Single(ending.Errors);
        var code = error.GetProperty("code").GetString();
        Assert.Equal(code, ending.Reason);
        return $"{code} {(int?)ending.Status}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
            return;
        }

        await _relay.DisposeAsync();
        _data?.Dispose();
        _temp?.Dispose();
    }
}

/// <summary>Waiting for something another thread does, in real time and never for a protocol limit.</summary>
internal static class SpeechWait
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        var spins = 0;
        while (!condition())
        {
            Assert.True(watch.Elapsed < Patience, $"Did not happen in time: {what}.");
            if (++spins < 200)
            {
                await Task.Yield();
            }
            else
            {
                await Task.Delay(1);
            }
        }
    }
}
