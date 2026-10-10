using System.Net;
using System.Text;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>"Reset pairing" on a running relay (protocol 4.4): the one action that revokes a token.</summary>
public sealed class PairingResetTests
{
    // Protocol P2-143.
    [Fact]
    public async Task The_old_token_is_401_and_the_new_one_200_without_a_restart()
    {
        await using var host = await RelayTestHost.StartAsync();
        var path = PairingFile.PathIn(host.Data.Path);
        var oldToken = host.Token;
        var oldFile = host.PairingFiles.Get(path);
        using var before = await host.GetAsync("/v1/health");
        host.Events.Clear();

        var qr = await host.Relay.ResetPairingAsync(CancellationToken.None);
        using var withOld = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", TestTokens.Bearer(oldToken)));
        var newToken = await host.ReadTokenAsync();
        using var withNew = await host.GetAsync("/v1/health");
        await host.Events.WaitForAsync<SelfCheckCompleted>();
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.NotEqual(oldToken, newToken);
        Assert.Matches(@"\A[A-Za-z0-9_-]{43}\z", newToken);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(
            """{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}""",
            await withOld.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
        Assert.Equal(RelayStatus.Running, host.Relay.Status);

        // The file was replaced by the new pairing, and holds neither token.
        var newFile = host.PairingFiles.Get(path)!;
        Assert.NotEqual(oldFile, newFile);
        Assert.Contains(PairingHarness.HashTextOf(newToken), Encoding.UTF8.GetString(newFile), StringComparison.Ordinal);
        Assert.DoesNotContain(oldToken, Encoding.UTF8.GetString(newFile), StringComparison.Ordinal);
        Assert.DoesNotContain(newToken, Encoding.UTF8.GetString(newFile), StringComparison.Ordinal);

        // The new QR is the one of the new token, and the desktop is told in this order.
        var payload = "{\"endpoint\":\"" + RelayTestHost.Endpoint + "\",\"token\":\"" + newToken + "\"}";
        Assert.Equal(QrMatrix.Encode(Encoding.UTF8.GetBytes(payload)), qr.Modules);
        var pairingEvents = host.Events.Snapshot()
            .Where(relayEvent => relayEvent is Notice or PairingShown or SelfCheckCompleted)
            .ToList();
        Assert.Equal(3, pairingEvents.Count);
        var notice = Assert.IsType<Notice>(pairingEvents[0]);
        Assert.Equal(NoticeKind.PairingReset, notice.Kind);
        Assert.Equal("Pairing reset. Every iPhone must pair again.", notice.Message);
        Assert.Equal(RelayTestHost.Endpoint, Assert.IsType<PairingShown>(pairingEvents[1]).Endpoint);
        Assert.Equal(SelfCheckResult.Verified, Assert.IsType<SelfCheckCompleted>(pairingEvents[2]).Result);

        // The self-check after a reset already uses the new token.
        Assert.Equal(newToken, Assert.Single(host.SelfCheck.Calls).Token);
    }

    [Fact]
    public async Task A_second_reset_revokes_the_first_new_token_too()
    {
        await using var host = await RelayTestHost.StartAsync();
        var original = host.Token;

        await host.Relay.ResetPairingAsync(CancellationToken.None);
        var first = await host.ReadTokenAsync();
        await host.Relay.ResetPairingAsync(CancellationToken.None);
        var second = await host.ReadTokenAsync();

        Assert.Equal(3, new[] { original, first, second }.Distinct().Count());
        foreach (var revoked in new[] { original, first })
        {
            using var response = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", TestTokens.Bearer(revoked)));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var accepted = await host.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    // Protocol 4.4: the file is replaced first; when that fails, nothing changed.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_failed_write_keeps_the_old_pairing(bool writesFail, bool protectorFails)
    {
        await using var host = await RelayTestHost.StartAsync();
        var path = PairingFile.PathIn(host.Data.Path);
        var oldToken = host.Token;
        var oldFile = host.PairingFiles.Get(path);
        var writes = host.PairingFiles.Writes;
        host.Events.Clear();
        host.PairingFiles.WritesFail = writesFail;
        host.Protector.Fails = protectorFails;

        var exception = await Assert.ThrowsAsync<IOException>(
            () => host.Relay.ResetPairingAsync(CancellationToken.None));

        host.PairingFiles.WritesFail = false;
        host.Protector.Fails = false;
        using var withOld = await host.GetAsync("/v1/health");
        await host.Events.SettleAsync();

        // The fixed text, with nothing of the cause.
        Assert.Equal("The pairing could not be saved.", exception.Message);
        Assert.Null(exception.InnerException);

        // The old pairing is still the accepted one, the file is the old file, and nobody was told
        // about a reset or shown a QR.
        Assert.Equal(HttpStatusCode.OK, withOld.StatusCode);
        Assert.Equal(oldToken, await host.ReadTokenAsync());
        Assert.Equal(oldFile, host.PairingFiles.Get(path));
        Assert.Equal(writes, host.PairingFiles.Writes);
        Assert.Empty(host.Events.OfType<Notice>());
        Assert.Empty(host.Events.OfType<PairingShown>());
        Assert.Empty(host.SelfCheck.Calls);
        Assert.Equal(RelayStatus.Running, host.Relay.Status);

        // And a later reset works.
        await host.Relay.ResetPairingAsync(CancellationToken.None);
        Assert.NotEqual(oldToken, await host.ReadTokenAsync());
    }

    // Protocol P2-145 at the level of this area: nothing pairing publishes carries the token or a nonce.
    [Fact]
    public async Task No_event_of_show_reset_and_self_check_carries_the_token_or_the_nonce()
    {
        await using var host = await RelayTestHost.StartAsync();
        var first = host.Token;

        await host.Relay.ShowPairingAsync(CancellationToken.None);
        await host.Relay.ResetPairingAsync(CancellationToken.None);
        var second = await host.ReadTokenAsync();
        await PairingHarness.UntilAsync(() => host.Events.Count<SelfCheckCompleted>() == 2);
        await host.Events.SettleAsync();

        var calls = host.SelfCheck.Calls;
        Assert.Equal(2, calls.Count);
        Assert.NotEqual(calls[0].Nonce, calls[1].Nonce);
        var texts = host.Events.Snapshot().Select(relayEvent => relayEvent.ToString()).ToList();
        Assert.Contains(texts, text => text.Contains(nameof(PairingShown), StringComparison.Ordinal));
        Assert.All(texts, text =>
        {
            Assert.DoesNotContain(first, text, StringComparison.Ordinal);
            Assert.DoesNotContain(second, text, StringComparison.Ordinal);
            Assert.DoesNotContain(calls[0].Nonce, text, StringComparison.Ordinal);
            Assert.DoesNotContain(calls[1].Nonce, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", text, StringComparison.Ordinal);
        });
    }
}
