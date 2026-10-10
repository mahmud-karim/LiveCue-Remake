using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The pairing area as the edge, the host, the answers and the speech bridge use it.
///
/// First version (foundation): a stand-in with no storage. It creates a token in memory at start,
/// verifies it, exports the payload, returns a fixed 21 x 21 matrix that is not a readable code, and on
/// reset replaces the token and cancels the revocation token. The accepted hash and its revocation source
/// are one object, swapped as a whole, as W-design 4.7 says for the real service. No self-check runs:
/// every show reports "not verified". The pairing file, DPAPI, the real matrix and the self-check are
/// work package WP-W02.
/// </summary>
internal sealed class PairingService : IPairingService
{
    private readonly RelayContext _context;
    private readonly Lock _gate = new();
    private AcceptedPairing? _accepted;

    public PairingService(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Task<bool> LoadOrCreateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            // A start is not a reset: the revocation source of an earlier start stays the same one.
            Volatile.Write(ref _accepted, AcceptedPairing.Create(_accepted?.Source));
        }

        return Task.FromResult(true);
    }

    public bool TryVerify(ReadOnlySpan<char> token, out CancellationToken revoked)
    {
        // One read: the hash and the revocation token belong to the same pairing.
        var accepted = Volatile.Read(ref _accepted);
        if (accepted is not null && PairingToken.Matches(token, accepted.Hash))
        {
            revoked = accepted.Revoked;
            return true;
        }

        revoked = CancellationToken.None;
        return false;
    }

    public bool IsSelfCheckNonce(ReadOnlySpan<char> headerValue) => false;

    public void NoteSelfCheckArrival(bool throughFunnel)
    {
    }

    public Task<PairingQr> ShowAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Show());
    }

    public Task<PairingQr> ResetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        AcceptedPairing? previous;
        lock (_gate)
        {
            previous = _accepted;
            Volatile.Write(ref _accepted, AcceptedPairing.Create(source: null));
        }

        // First the swap, then the cancellation: in-flight answers and open streams of the old token end
        // through this, and a request verified against the old pairing holds exactly this token.
        if (previous is not null)
        {
            previous.Source.Cancel();
            previous.Source.Dispose();
        }

        _context.Events.Publish(new Notice(_context.Events.Now, NoticeKind.PairingReset, FixedTexts.PairingReset));
        return Task.FromResult(Show());
    }

    public Task<string> GetPayloadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var accepted = Volatile.Read(ref _accepted) ?? throw new InvalidOperationException("No pairing exists yet.");
        return Task.FromResult(PairingPayload.Build(_context.AdvertisedEndpoint, accepted.Token));
    }

    private PairingQr Show()
    {
        var events = _context.Events;
        events.Publish(new PairingShown(events.Now, _context.AdvertisedEndpoint));
        events.Publish(new SelfCheckCompleted(events.Now, SelfCheckResult.NotVerified));
        return new PairingQr(_context.AdvertisedEndpoint, QrMatrix.Placeholder());
    }

    /// <summary>The pairing accepted right now: its token, its hash and its revocation, never read apart.
    /// The revocation token is taken once, so it stays usable after a reset disposed the source.</summary>
    private sealed class AcceptedPairing
    {
        private AcceptedPairing(string token, CancellationTokenSource source)
        {
            Token = token;
            Hash = PairingToken.Hash(token);
            Source = source;
            Revoked = source.Token;
        }

        public string Token { get; }

        public byte[] Hash { get; }

        public CancellationTokenSource Source { get; }

        public CancellationToken Revoked { get; }

        public static AcceptedPairing Create(CancellationTokenSource? source) =>
            new(PairingToken.Create(), source ?? new CancellationTokenSource());
    }
}
