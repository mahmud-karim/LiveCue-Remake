using System.Buffers.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The self-check of protocol 4.5 (W-design 4.7): each time a QR is shown the relay requests its own
/// advertised endpoint from the outside, <c>GET E + "v1/health"</c>, with a nonce made for that one
/// check. The edge recognises the inbound request by the nonce and does not count it as a phone.
///
/// At most W-07 checks are in progress at once; one more cancels the oldest, which is then reported
/// "not verified". A nonce exists in memory only, and only while its check runs.
/// </summary>
internal sealed class SelfCheck
{
    private const int NonceBytes = 16;

    // What IsNonce matched on this thread, for the NoteArrival that the edge makes right after it.
    [ThreadStatic]
    private static Check? _matched;

    private readonly RelayContext _context;
    private readonly ISelfCheckClient _client;
    private readonly Uri _healthUrl;
    private readonly Lock _gate = new();
    private readonly List<Check> _running = new(ProtocolConstants.MaxSelfChecks);

    public SelfCheck(RelayContext context, ISelfCheckClient client)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(client);
        _context = context;
        _client = client;

        // Text concatenation, as the phone builds its URLs (protocol section 3).
        _healthUrl = new Uri(context.AdvertisedEndpoint + "v1/health", UriKind.Absolute);
    }

    /// <summary>
    /// True when <paramref name="headerValue"/> is the nonce of a check in progress. Every comparison is
    /// constant time. Allocates nothing.
    /// </summary>
    public bool IsNonce(ReadOnlySpan<char> headerValue)
    {
        var candidate = MemoryMarshal.AsBytes(headerValue);
        Check? match = null;
        lock (_gate)
        {
            foreach (var check in _running)
            {
                if (CryptographicOperations.FixedTimeEquals(candidate, MemoryMarshal.AsBytes(check.Nonce.AsSpan())))
                {
                    match = check;
                }
            }
        }

        _matched = match;
        return match is not null;
    }

    /// <summary>
    /// The edge saw the request of the check whose nonce it has just matched, and says whether that
    /// request carried <c>Tailscale-Funnel-Request</c>.
    /// </summary>
    public void NoteArrival(bool throughFunnel)
    {
        var check = _matched;
        _matched = null;
        if (check is null || !ReferenceEquals(check.Owner, this))
        {
            // Not the thread that matched the nonce. With one check in progress it can only be that one.
            lock (_gate)
            {
                check = _running.Count == 1 ? _running[0] : null;
            }
        }

        if (check is not null && throughFunnel)
        {
            check.MarkThroughFunnel();
        }
    }

    /// <summary>
    /// Starts one check in the background and returns at once. Its result arrives as
    /// <see cref="SelfCheckCompleted"/>. The token is copied; the copy is cleared when the check ends.
    /// </summary>
    /// <returns>The running check, for tests. It never faults.</returns>
    public Task Start(ReadOnlySpan<byte> token)
    {
        var copy = token.ToArray();
        return Task.Run(() => RunAsync(copy));
    }

    /// <summary>One check, from its nonce to its event. Takes ownership of <paramref name="token"/>.</summary>
    private async Task RunAsync(byte[] token)
    {
        var stopping = _context.Access.Stopping;
        using var limit = new CancellationTokenSource(ProtocolConstants.SelfCheckTimeout, _context.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, stopping);
        var check = new Check(this, CreateNonce(), limit);
        var verified = false;
        try
        {
            Register(check);
            verified = await _client
                .GetHealthAsync(_healthUrl, PairingToken.ToText(token), check.Nonce, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The limit C-49 passed, a newer check took this one's place, or the relay is stopping.
        }
#pragma warning disable CA1031 // A background check must end with its event, whatever the client seam did.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _context.Events.Diagnostic("pairing", "The connection check failed unexpectedly.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
            Unregister(check);
        }

        if (stopping.IsCancellationRequested)
        {
            // Nobody is left to tell: the relay's last event is RelayStopped.
            return;
        }

        var result = !verified
            ? SelfCheckResult.NotVerified
            : check.ThroughFunnel ? SelfCheckResult.VerifiedThroughFunnel : SelfCheckResult.Verified;
        _context.Events.Publish(new SelfCheckCompleted(_context.Events.Now, result));
    }

    private void Register(Check check)
    {
        Check? oldest = null;
        lock (_gate)
        {
            if (_running.Count >= ProtocolConstants.MaxSelfChecks)
            {
                oldest = _running[0];
                _running.RemoveAt(0);
            }

            _running.Add(check);
        }

        // Outside the lock: cancelling runs the oldest check's continuations.
        oldest?.Supersede();
    }

    private void Unregister(Check check)
    {
        lock (_gate)
        {
            _running.Remove(check);
        }
    }

    private static string CreateNonce()
    {
        Span<byte> random = stackalloc byte[NonceBytes];
        RandomNumberGenerator.Fill(random);
        return Base64Url.EncodeToString(random);
    }

    /// <summary>One check in progress: its nonce, and what the edge reported about its request.</summary>
    private sealed class Check(SelfCheck owner, string nonce, CancellationTokenSource limit)
    {
        private volatile bool _throughFunnel;

        public SelfCheck Owner { get; } = owner;

        public string Nonce { get; } = nonce;

        public bool ThroughFunnel => _throughFunnel;

        public void MarkThroughFunnel() => _throughFunnel = true;

        /// <summary>A newer check took this one's place (W-07). It ends as "not verified".</summary>
        public void Supersede()
        {
            try
            {
                limit.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // It ended by itself in the meantime.
            }
        }
    }
}
