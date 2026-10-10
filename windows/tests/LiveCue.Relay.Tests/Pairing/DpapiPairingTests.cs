using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// The pairing store with the real protector: DPAPI for the current Windows user with the fixed entropy
/// of architecture 3.6, on real files in a temporary folder. These tests need Windows.
/// </summary>
public sealed class DpapiPairingTests
{
    // Protocol P2-141, with the production seams.
    [Fact]
    public async Task Create_then_load_with_the_real_protector()
    {
        using var harness = new PairingHarness(realFiles: true, new DpapiPairingProtector());

        Assert.True(await harness.Service.LoadOrCreateAsync(CancellationToken.None));
        var token = await PairingHarness.TokenOfAsync(harness.Service);
        var file = await File.ReadAllBytesAsync(harness.PairingPath);
        var restarted = harness.NewService();
        var loadedAgain = !await restarted.LoadOrCreateAsync(CancellationToken.None);

        Assert.True(loadedAgain);
        Assert.Equal(token, await PairingHarness.TokenOfAsync(restarted));
        Assert.True(restarted.TryVerify(token, out _));
        Assert.Equal(file, await File.ReadAllBytesAsync(harness.PairingPath));

        // The file holds the hash and a DPAPI blob: not the token, in no plain form.
        var text = Encoding.UTF8.GetString(file);
        using var document = JsonDocument.Parse(file);
        var blob = document.RootElement.GetProperty("protectedToken").GetBytesFromBase64();
        Assert.Equal(PairingHarness.HashTextOf(token), document.RootElement.GetProperty("tokenHash").GetString());
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.True(blob.AsSpan().IndexOf(Encoding.ASCII.GetBytes(token)) < 0);
        Assert.True(blob.Length > token.Length);
        Assert.Equal([PairingFile.Name], harness.Data.FileNames());

        // The same token is shown again, from the blob.
        var qr = await restarted.ShowAsync(CancellationToken.None);
        Assert.Equal(
            QrMatrix.Encode(Encoding.ASCII.GetBytes("{\"endpoint\":\"" + PairingHarness.Endpoint + "\",\"token\":\"" + token + "\"}")),
            qr.Modules);
    }

    // Test plan S-37: a copied pairing file from another Windows account does not load. DPAPI refuses
    // such a blob the same way it refuses the ones made here.
    [Theory]
    [InlineData("protected with another entropy")]
    [InlineData("damaged")]
    [InlineData("not a blob")]
    public async Task A_blob_that_does_not_unprotect_is_unreadable(string kind)
    {
        using var harness = new PairingHarness(realFiles: true, new DpapiPairingProtector());
        var token = Encoding.ASCII.GetBytes(TestTokens.Example);
        var blob = kind switch
        {
            "protected with another entropy" =>
                ProtectedData.Protect(token, "another application"u8.ToArray(), DataProtectionScope.CurrentUser),
            "damaged" => Damaged(new DpapiPairingProtector().Protect(token)),
            _ => RandomNumberGenerator.GetBytes(230),
        };
        var contents = PairingFile.Write(SHA256.HashData(token), blob, ManualTimeProvider.DefaultStart);
        await File.WriteAllBytesAsync(harness.PairingPath, contents);

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => harness.Service.LoadOrCreateAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, exception.Code);
        Assert.Equal("The saved pairing could not be read.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.False(harness.Service.TryVerify(TestTokens.Example, out _));
        Assert.Equal(contents, await File.ReadAllBytesAsync(harness.PairingPath));
        Assert.Equal([PairingFile.Name], harness.Data.FileNames());
    }

    [Fact]
    public void The_protector_round_trips_and_fails_with_its_fixed_exception()
    {
        var protector = new DpapiPairingProtector();
        var token = Encoding.ASCII.GetBytes(TestTokens.Example);

        var blob = protector.Protect(token);

        Assert.Equal(token, protector.Unprotect(blob));
        Assert.NotEqual(blob, protector.Protect(token));
        var exception = Assert.Throws<PairingProtectionException>(() => protector.Unprotect(Damaged(blob)));
        Assert.Equal("Protected pairing storage is not available.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Throws<PairingProtectionException>(() => protector.Unprotect(RandomNumberGenerator.GetBytes(64)));
    }

    // The public entry points with nothing injected, as the supervisor and the console host call them.
    [Fact]
    public async Task The_stored_reset_and_the_stored_export_work_with_the_production_seams()
    {
        using var data = new TempFolder();
        await File.WriteAllTextAsync(PairingFile.PathIn(data.Path), "unreadable");
        await Assert.ThrowsAsync<IOException>(
            () => StoredPairingExport.ReadPayloadAsync(data.Path, RelayTestHost.Endpoint, CancellationToken.None));

        await PairingMaintenance.ResetStoredPairingAsync(data.Path, CancellationToken.None);
        var payload = await StoredPairingExport.ReadPayloadAsync(data.Path, RelayTestHost.Endpoint, CancellationToken.None);

        Assert.NotNull(payload);
        var token = PairingHarness.TokenIn(payload);
        Assert.Matches(@"\A[A-Za-z0-9_-]{43}\z", token);
        Assert.Equal([PairingFile.Name], data.FileNames());

        // A relay started on that folder restores the pairing and accepts that token.
        var options = new RelayOptions(
            0, RelayTestHost.Endpoint, ConnectionMode.Private, data.Path, null, null, 47842, RelayTestHost.RelayVersion, false);
        await using var relay = RelayFactory.Create(
            options, new RelayDependencies { SelfCheckClient = new FakeSelfCheckClient() });
        await relay.StartAsync(CancellationToken.None);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{relay.BoundPort}") };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        request.Headers.TryAddWithoutValidation("Authorization", TestTokens.Bearer(token));
        request.Headers.TryAddWithoutValidation("X-LiveCue-Protocol", "2");
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await ((IPairingExport)relay).GetPairingPayloadAsync(CancellationToken.None));
    }

    private static byte[] Damaged(byte[] blob)
    {
        var copy = (byte[])blob.Clone();
        for (var index = copy.Length / 2; index < copy.Length; index++)
        {
            copy[index] ^= 0xFF;
        }

        return copy;
    }
}
