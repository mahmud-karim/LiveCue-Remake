using System.Collections.Concurrent;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// Test plan ST-12 (security review SEC-07): the accepted hash and its revocation token are one object.
/// A request that was verified against a pairing holds the revocation token of exactly that pairing,
/// whenever a reset runs: before the verification, after it, or in the middle of it.
/// </summary>
public sealed class PairingRevocationTests
{
    [Fact]
    public async Task A_reset_between_verification_and_hand_over_still_cancels_the_request()
    {
        using var harness = new PairingHarness();
        var service = harness.Service;
        await service.LoadOrCreateAsync(CancellationToken.None);
        var token = await PairingHarness.TokenOfAsync(service);

        // An ordinary verification first: its revocation token is alive until a reset.
        Assert.True(service.TryVerify(token, out var earlier));
        Assert.False(earlier.IsCancellationRequested);

        // The hook runs inside TryVerify, after the comparison said "accepted" and before the revocation
        // token is handed out. A whole reset runs to its end in that gap.
        var resets = 0;
        service.AfterComparison = () =>
        {
            service.AfterComparison = null;
            Complete(service.ResetAsync(CancellationToken.None));
            resets++;
        };

        var verified = service.TryVerify(token, out var revoked);

        // The verification was made against the old pairing, so it is the old pairing's token that
        // comes back, and the reset that ran in the gap has cancelled it. Work linked to it ends at once.
        Assert.Equal(1, resets);
        Assert.True(verified);
        Assert.True(revoked.IsCancellationRequested);
        Assert.Equal(earlier, revoked);
        using (var work = CancellationTokenSource.CreateLinkedTokenSource(revoked))
        {
            Assert.True(work.IsCancellationRequested);
        }

        // From then on the old token is refused, and the new one comes with a live revocation token.
        Assert.False(service.TryVerify(token, out var none));
        Assert.Equal(CancellationToken.None, none);
        var newToken = await PairingHarness.TokenOfAsync(service);
        Assert.NotEqual(token, newToken);
        Assert.True(service.TryVerify(newToken, out var current));
        Assert.False(current.IsCancellationRequested);
        Assert.NotEqual(revoked, current);
    }

    [Fact]
    public async Task A_reset_cancels_the_token_of_every_request_verified_before_it()
    {
        using var harness = new PairingHarness();
        var service = harness.Service;
        await service.LoadOrCreateAsync(CancellationToken.None);
        var token = await PairingHarness.TokenOfAsync(service);
        Assert.True(service.TryVerify(token, out var first));
        Assert.True(service.TryVerify(token, out var second));
        var ended = 0;
        using var registration = first.Register(() => Interlocked.Increment(ref ended));

        Assert.Equal(first, second);
        Assert.False(service.TryVerify(new string('x', 43), out var refused));
        Assert.Equal(CancellationToken.None, refused);

        // A callback that throws does not stop the reset, and is reported without its message.
        using var throwing = first.Register(() => throw new InvalidOperationException("PRIVATE DIAGNOSTIC"));
        await service.ResetAsync(CancellationToken.None);

        Assert.True(first.IsCancellationRequested);
        Assert.Equal(1, Volatile.Read(ref ended));
        Assert.False(service.TryVerify(token, out _));
        var diagnostic = Assert.Single(harness.Events.OfType<Diagnostic>());
        Assert.Equal("pairing", diagnostic.Source);
        Assert.DoesNotContain("PRIVATE DIAGNOSTIC", diagnostic.Message, StringComparison.Ordinal);
        Assert.Single(harness.Events.OfType<Notice>());
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Ten_thousand_resets_against_continuous_requests_leave_nothing_running_on_an_old_token()
    {
        const int Resets = 10_000;
        using var harness = new PairingHarness();
        var service = harness.Service;
        await service.LoadOrCreateAsync(CancellationToken.None);
        var shared = new SharedToken { Text = await PairingHarness.TokenOfAsync(service) };

        // Every revocation token a "request" was ever handed, with the token text it was admitted with.
        // The requests run on threads of their own, as fast as they can, for as long as the resets last.
        var admitted = new ConcurrentDictionary<CancellationToken, string>();
        var mixed = 0;
        using var stop = new CancellationTokenSource();
        var requests = Enumerable.Range(0, 2).Select(_ => Task.Factory.StartNew(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var presented = shared.Text;
                    if (service.TryVerify(presented, out var revoked)
                        && !ReferenceEquals(admitted.GetOrAdd(revoked, presented), presented))
                    {
                        // One revocation token came back for two different tokens.
                        Interlocked.Increment(ref mixed);
                    }
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)).ToArray();

        for (var reset = 0; reset < Resets; reset++)
        {
            await service.ResetAsync(CancellationToken.None);
            shared.Text = await PairingHarness.TokenOfAsync(service);
        }

        var last = shared.Text;
        Assert.True(service.TryVerify(last, out var live));
        admitted.TryAdd(live, last);
        await stop.CancelAsync();
        await Task.WhenAll(requests);

        // One revocation token per pairing, never shared by two.
        Assert.Equal(0, Volatile.Read(ref mixed));
        Assert.Equal(admitted.Count, admitted.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.True(admitted.Count > Resets / 10, $"Only {admitted.Count} pairings were ever verified.");

        // Whatever was admitted with a token that is no longer the accepted one holds a cancelled
        // revocation token. Only the last pairing's is alive.
        foreach (var (revoked, presented) in admitted)
        {
            Assert.Equal(presented != last, revoked.IsCancellationRequested);
        }

        Assert.False(live.IsCancellationRequested);
        Assert.Equal(Resets, harness.Events.OfType<Notice>().Count);
    }

    private static void Complete(Task task) => task.GetAwaiter().GetResult();

    private sealed class SharedToken
    {
        private volatile string _text = string.Empty;

        public string Text
        {
            get => _text;
            set => _text = value;
        }
    }
}
