using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveCue.Relay.Core;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// The pairing area on its own, without a listener: a <see cref="PairingService"/> over a temporary data
/// folder, the manual clock, a fake protector, a scripted self-check client and a list of the events it
/// published. The pairing file is in memory unless the test asks for the real file system.
/// </summary>
internal sealed class PairingHarness : IDisposable
{
    public const string Endpoint = RelayTestHost.Endpoint;

    private readonly IPairingFiles _files;
    private readonly IPairingProtector _protector;
    private readonly string _endpoint;

    /// <param name="realFiles">True: <c>pairing.json</c> is a real file in <see cref="Data"/>.</param>
    /// <param name="protector">Null means <see cref="Protector"/>, the fake one.</param>
    /// <param name="endpoint">The advertised endpoint, already canonical.</param>
    public PairingHarness(bool realFiles = false, IPairingProtector? protector = null, string endpoint = Endpoint)
    {
        _files = realFiles ? new PairingFiles() : Files;
        _protector = protector ?? Protector;
        _endpoint = endpoint;
        Events = new RecordingSink(Clock);
        Service = NewService();
    }

    public ManualTimeProvider Clock { get; } = new();

    public TempFolder Data { get; } = new();

    public FakePairingProtector Protector { get; } = new();

    public InMemoryPairingFiles Files { get; } = new();

    public FakeSelfCheckClient SelfCheck { get; } = new();

    public StoppingState Access { get; } = new();

    public RecordingSink Events { get; }

    /// <summary>The service under test. Nothing was loaded yet.</summary>
    public PairingService Service { get; }

    /// <summary>Where the pairing file is, for either file system.</summary>
    public string PairingPath => PairingFile.PathIn(Data.Path);

    /// <summary>Another instance over the same folder and seams, as the next relay start makes one.</summary>
    public PairingService NewService() => new(new RelayContext(
        new RelayOptions(
            Port: 0,
            AdvertisedEndpoint: _endpoint,
            Mode: ConnectionMode.Private,
            DataDirectory: Data.Path,
            CodexExecutablePath: null,
            LabFolder: null,
            LabPort: 47842,
            RelayVersion: RelayTestHost.RelayVersion,
            WarmCatalogOnStart: false),
        new RelayDependencies
        {
            Clock = Clock,
            PairingProtector = _protector,
            PairingFiles = _files,
            SelfCheckClient = SelfCheck,
        },
        Events,
        Access,
        new FakeProcessRunner(),
        _endpoint));

    /// <summary>The bytes of the pairing file, or null when there is none.</summary>
    public byte[]? ReadPairingFile() => _files.Exists(PairingPath) ? _files.ReadAllBytes(PairingPath) : null;

    /// <summary>The token a service accepts, read through its payload export.</summary>
    public static async Task<string> TokenOfAsync(IPairingService service) =>
        TokenIn(await service.GetPayloadAsync(CancellationToken.None));

    /// <summary>The <c>token</c> member of a payload text.</summary>
    public static string TokenIn(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("The pairing payload has no token.");
    }

    /// <summary>The 64 lower-case hex characters the pairing file holds for a token.</summary>
    public static string HashTextOf(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    /// <summary>A pairing file for that token as this protector protects it, written by the relay's own writer.</summary>
    public static byte[] FileFor(string token, IPairingProtector protector, string? tokenHash = null)
    {
        var bytes = Encoding.ASCII.GetBytes(token);
        var hash = Convert.FromHexString(tokenHash ?? HashTextOf(token));
        return PairingFile.Write(hash, protector.Protect(bytes), ManualTimeProvider.DefaultStart);
    }

    /// <summary>Waits, in real time, until something that happens in the background has happened.</summary>
    public static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("What the test waits for did not happen in time.");
            }

            await Task.Delay(5);
        }
    }

    public void Dispose()
    {
        Access.Dispose();
        Data.Dispose();
    }
}

/// <summary>Every event a pairing service published, in order.</summary>
internal sealed class RecordingSink(TimeProvider clock) : IRelayEventSink
{
    private readonly Lock _gate = new();
    private readonly List<RelayEvent> _events = [];

    public DateTimeOffset Now => clock.GetUtcNow();

    public void Publish(RelayEvent relayEvent)
    {
        lock (_gate)
        {
            _events.Add(relayEvent);
        }
    }

    public void Diagnostic(string source, string fixedText, Exception? exception = null) => Publish(
        new Diagnostic(Now, source, exception is null ? fixedText : $"{fixedText} ({exception.GetType().Name})"));

    public IReadOnlyList<RelayEvent> Snapshot()
    {
        lock (_gate)
        {
            return [.. _events];
        }
    }

    public IReadOnlyList<T> OfType<T>()
        where T : RelayEvent => [.. Snapshot().OfType<T>()];

    /// <summary>Waits until at least <paramref name="count"/> events of that type were published.</summary>
    public async Task<IReadOnlyList<T>> WaitForAsync<T>(int count = 1)
        where T : RelayEvent
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var found = OfType<T>();
            if (found.Count >= count)
            {
                return found;
            }

            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"Only {found.Count} of {count} {typeof(T).Name} events arrived in time.");
            }

            await Task.Delay(5);
        }
    }
}

/// <summary>The relay's access state as far as pairing reads it: accepting, until the test stops it.</summary>
internal sealed class StoppingState : IAccessState, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    public bool Accepting => true;

    public CancellationToken WhileAccepting => CancellationToken.None;

    public CancellationToken Stopping => _stopping.Token;

    public void Stop() => _stopping.Cancel();

    public void Dispose() => _stopping.Dispose();
}
