using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The pairing area as the edge, the host, the answers and the speech bridge use it.
///
/// First version (foundation): a stand-in with no storage. It creates a token in memory at start,
/// verifies it, exports the payload, returns a fixed 21 x 21 matrix that is not a readable code, and on
/// reset replaces the token and cancels the revocation token. No self-check runs: every show reports
/// "not verified". The pairing file, DPAPI, the real matrix and the self-check are work package WP-W02.
/// </summary>
#pragma warning disable CA1001 // The revocation source lives as long as the relay; a reset cancels and disposes the old one.
internal sealed class PairingService : IPairingService
#pragma warning restore CA1001
{
    private readonly RelayContext _context;
    private readonly Lock _gate = new();
    private string? _token;
    private byte[] _acceptedHash = [];
    private CancellationTokenSource _revocation = new();

    public PairingService(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public CancellationToken CurrentRevocation
    {
        get
        {
            lock (_gate)
            {
                return _revocation.Token;
            }
        }
    }

    public Task<bool> LoadOrCreateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Accept(PairingToken.Create());
        return Task.FromResult(true);
    }

    public bool Verify(ReadOnlySpan<char> token) => PairingToken.Matches(token, Volatile.Read(ref _acceptedHash));

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

        CancellationTokenSource revoked;
        lock (_gate)
        {
            Accept(PairingToken.Create());
            revoked = _revocation;
            _revocation = new CancellationTokenSource();
        }

        // In-flight answers and open streams of the old token end through this.
        revoked.Cancel();
        revoked.Dispose();

        _context.Events.Publish(new Notice(_context.Events.Now, NoticeKind.PairingReset, FixedTexts.PairingReset));
        return Task.FromResult(Show());
    }

    public Task<string> GetPayloadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var token = Volatile.Read(ref _token) ?? throw new InvalidOperationException("No pairing exists yet.");
        return Task.FromResult(PairingPayload.Build(_context.AdvertisedEndpoint, token));
    }

    private void Accept(string token)
    {
        Volatile.Write(ref _acceptedHash, PairingToken.Hash(token));
        Volatile.Write(ref _token, token);
    }

    private PairingQr Show()
    {
        var events = _context.Events;
        events.Publish(new PairingShown(events.Now, _context.AdvertisedEndpoint));
        events.Publish(new SelfCheckCompleted(events.Now, SelfCheckResult.NotVerified));
        return new PairingQr(_context.AdvertisedEndpoint, QrMatrix.Placeholder());
    }
}
