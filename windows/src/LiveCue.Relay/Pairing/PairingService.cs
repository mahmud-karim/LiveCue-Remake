using System.Security.Cryptography;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The pairing area as the edge, the host, the answers and the speech bridge use it (W-design 4.7).
///
/// One shared token makes a phone trusted (protocol 4.1). The relay keeps its SHA-256 in memory and, in
/// <c>pairing.json</c>, that hash and a copy protected for the current Windows user. The token itself
/// is not kept between uses: it exists as bytes only while a pairing is created, loaded, shown, reset or
/// exported, and in a running self-check, and every such buffer is cleared.
///
/// The accepted hash and the revocation source of that pairing are the two members of one immutable
/// object, held in one field. <see cref="TryVerify"/> reads the field once; a reset swaps the object and
/// then cancels the source of the one it took out. A request can therefore never be verified against one
/// pairing and linked to the revocation token of the next.
/// </summary>
internal sealed class PairingService : IPairingService
{
    private readonly RelayContext _context;
    private readonly PairingStore _store;
    private readonly SelfCheck _selfCheck;

    // Load, show, reset and export run one at a time.
    private readonly Lock _gate = new();

    private AcceptedPairing? _accepted;

    public PairingService(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _store = new PairingStore(
            context.Options.DataDirectory,
            context.Dependencies.PairingProtector ?? new DpapiPairingProtector(),
            context.Dependencies.PairingFiles ?? new PairingFiles(),
            context.Clock);
        _selfCheck = new SelfCheck(context, context.Dependencies.SelfCheckClient ?? new SelfCheckClient());
    }

    /// <summary>
    /// Tests only (test plan ST-12): called inside <see cref="TryVerify"/> after the comparison succeeded
    /// and before the revocation token is handed out, so that a test can run a reset in exactly that gap.
    /// </summary>
    internal Action? AfterComparison { get; set; }

    public Task<bool> LoadOrCreateAsync(CancellationToken ct) => Serialised(LoadOrCreate, ct);

    public bool TryVerify(ReadOnlySpan<char> token, out CancellationToken revoked)
    {
        // One read: the hash and the revocation token belong to the same pairing.
        var accepted = Volatile.Read(ref _accepted);
        if (accepted is null || !PairingToken.Matches(token, accepted.Hash))
        {
            revoked = CancellationToken.None;
            return false;
        }

        AfterComparison?.Invoke();
        revoked = accepted.Revoked;
        return true;
    }

    public bool IsSelfCheckNonce(ReadOnlySpan<char> headerValue) => _selfCheck.IsNonce(headerValue);

    public void NoteSelfCheckArrival(bool throughFunnel) => _selfCheck.NoteArrival(throughFunnel);

    public Task<PairingQr> ShowAsync(CancellationToken ct) => Serialised(Show, ct);

    public Task<PairingQr> ResetAsync(CancellationToken ct) => Serialised(Reset, ct);

    public Task<string> GetPayloadAsync(CancellationToken ct) => Serialised(GetPayload, ct);

    /// <summary>
    /// Runs one piece of pairing work on the thread pool, after the work before it has ended. The work
    /// reads or writes the file and calls DPAPI, so it never runs on the caller's thread.
    /// </summary>
    private Task<T> Serialised<T>(Func<T> work, CancellationToken ct) => Task.Run(
        () =>
        {
            lock (_gate)
            {
                return work();
            }
        },
        ct);

    private bool LoadOrCreate()
    {
        Span<byte> token = stackalloc byte[PairingToken.Length];
        var hash = new byte[PairingToken.HashLength];
        try
        {
            switch (_store.Load(token, hash))
            {
                case PairingLoadResult.Loaded:
                    Volatile.Write(ref _accepted, new AcceptedPairing(hash, new CancellationTokenSource()));
                    return false;

                case PairingLoadResult.Missing:
                    // The only case in which a pairing is created without being asked for.
                    _store.Create(token, hash);
                    Volatile.Write(ref _accepted, new AcceptedPairing(hash, new CancellationTokenSource()));
                    return true;

                default:
                    // The file stays as it is, and no token is created (protocol 4.4).
                    throw Unreadable();
            }
        }
        catch (IOException)
        {
            throw Unreadable();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
        }
    }

    private PairingQr Show()
    {
        Span<byte> token = stackalloc byte[PairingToken.Length];
        try
        {
            ReadStoredToken(token);
            return Produce(token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
        }
    }

    private PairingQr Reset()
    {
        Span<byte> token = stackalloc byte[PairingToken.Length];
        var hash = new byte[PairingToken.HashLength];
        try
        {
            // First the file. When this throws, nothing changed: the old pairing stays the accepted one.
            _store.Create(token, hash);

            // Then the swap, then the cancellation: whatever passed E-3 with the old token, whenever it
            // read the old object, holds exactly the token that is cancelled here.
            var previous = Interlocked.Exchange(ref _accepted, new AcceptedPairing(hash, new CancellationTokenSource()));
            previous?.Revoke(_context.Events);

            _context.Events.Publish(new Notice(_context.Events.Now, NoticeKind.PairingReset, FixedTexts.PairingReset));
            return Produce(token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
        }
    }

    private string GetPayload()
    {
        Span<byte> token = stackalloc byte[PairingToken.Length];
        byte[]? payload = null;
        try
        {
            ReadStoredToken(token);
            payload = PairingPayload.Utf8(_context.AdvertisedEndpoint, token);
            return PairingPayload.Text(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    /// <summary>
    /// Reads and verifies the file as a load does (one decryption), and requires it to hold the pairing
    /// this relay accepts: a QR for any other token would pair nothing.
    /// </summary>
    private void ReadStoredToken(Span<byte> token)
    {
        var accepted = Volatile.Read(ref _accepted) ?? throw new InvalidOperationException("No pairing has been loaded.");
        Span<byte> hash = stackalloc byte[PairingToken.HashLength];
        if (_store.Load(token, hash) != PairingLoadResult.Loaded
            || !CryptographicOperations.FixedTimeEquals(hash, accepted.Hash))
        {
            CryptographicOperations.ZeroMemory(token);
            throw new IOException(PairingStore.UnreadableMessage);
        }
    }

    /// <summary>The matrix of the payload for this token, the PairingShown event, and one self-check.</summary>
    private PairingQr Produce(ReadOnlySpan<byte> token)
    {
        var endpoint = _context.AdvertisedEndpoint;
        var payload = PairingPayload.Utf8(endpoint, token);
        bool[][] modules;
        try
        {
            modules = QrMatrix.Encode(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        _context.Events.Publish(new PairingShown(_context.Events.Now, endpoint));

        // In the background: its result arrives as SelfCheckCompleted, and the QR is shown either way.
        _ = _selfCheck.Start(token);
        return new PairingQr(endpoint, modules);
    }

    private static RelayStartException Unreadable() =>
        new(RelayFaultCode.PairingStoreUnreadable, FixedTexts.Fault(RelayFaultCode.PairingStoreUnreadable));

    /// <summary>
    /// The pairing accepted right now: its hash and its revocation, never read apart. The revocation
    /// token is taken once, at construction. The source is cancelled when a reset replaces this pairing
    /// and is deliberately not disposed: it holds no timer, and requests may still hold its token.
    /// </summary>
    private sealed class AcceptedPairing
    {
        private readonly CancellationTokenSource _source;

        public AcceptedPairing(byte[] hash, CancellationTokenSource source)
        {
            Hash = hash;
            _source = source;
            Revoked = source.Token;
        }

        /// <summary>SHA-256 of the accepted token. Never changed after construction.</summary>
        public byte[] Hash { get; }

        /// <summary>Cancelled when a reset replaces this pairing.</summary>
        public CancellationToken Revoked { get; }

        /// <summary>
        /// Ends everything that was admitted with this pairing: in-flight answers and open speech
        /// streams observe the token. A callback that throws does not stop the reset.
        /// </summary>
        public void Revoke(IRelayEventSink events)
        {
            try
            {
                _source.Cancel();
            }
            catch (AggregateException exception)
            {
                events.Diagnostic("pairing", "A request did not end cleanly when pairing was reset.", exception);
            }
        }
    }
}
