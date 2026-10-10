using LiveCue.Relay.Pairing;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// The two pieces of pairing work that need no relay (architecture 3.4): the stored reset behind
/// <see cref="PairingMaintenance"/>, which is the recovery from an unreadable pairing file, and the
/// stored export behind <see cref="StoredPairingExport"/>, which only reads.
/// </summary>
public sealed class PairingMaintenanceTests
{
    private const string Endpoint = RelayTestHost.Endpoint;

    // Architecture 3.4.
    [Theory]
    [InlineData("this is not a pairing file")]
    [InlineData("")]
    [InlineData("{\"schemaVersion\":1}")]
    public async Task Reset_replaces_an_unreadable_file(string unreadable)
    {
        using var data = new TempFolder();
        var protector = new FakePairingProtector();
        var path = PairingFile.PathIn(data.Path);
        await File.WriteAllTextAsync(path, unreadable);
        await Assert.ThrowsAsync<IOException>(() => ReadAsync(data, protector));

        await StoredPairingReset.RunAsync(data.Path, protector, files: null, CancellationToken.None);

        // The file is a pairing again, written whole, and nothing else is in the folder.
        var payload = await ReadAsync(data, protector);
        Assert.NotNull(payload);
        var token = PairingHarness.TokenIn(payload);
        Assert.Matches(@"\A[A-Za-z0-9_-]{43}\z", token);
        Assert.Equal("{\"endpoint\":\"" + Endpoint + "\",\"token\":\"" + token + "\"}", payload);
        Assert.Equal([PairingFile.Name], data.FileNames());
        Assert.DoesNotContain(token, await File.ReadAllTextAsync(path), StringComparison.Ordinal);

        // The next start loads it instead of creating one, and accepts exactly that token.
        using var harness = new PairingHarness(realFiles: true, protector);
        File.Copy(path, harness.PairingPath);
        Assert.False(await harness.Service.LoadOrCreateAsync(CancellationToken.None));
        Assert.True(harness.Service.TryVerify(token, out _));
    }

    [Fact]
    public async Task Reset_replaces_a_readable_file_too_and_the_old_token_is_gone()
    {
        using var data = new TempFolder();
        var protector = new FakePairingProtector();
        var path = PairingFile.PathIn(data.Path);
        await File.WriteAllBytesAsync(path, PairingHarness.FileFor(TestTokens.Example, protector));
        Assert.Equal(TestTokens.Example, PairingHarness.TokenIn((await ReadAsync(data, protector))!));

        await StoredPairingReset.RunAsync(data.Path, protector, files: null, CancellationToken.None);
        var first = PairingHarness.TokenIn((await ReadAsync(data, protector))!);
        await StoredPairingReset.RunAsync(data.Path, protector, files: null, CancellationToken.None);
        var second = PairingHarness.TokenIn((await ReadAsync(data, protector))!);

        Assert.NotEqual(TestTokens.Example, first);
        Assert.NotEqual(first, second);
        Assert.Equal([PairingFile.Name], data.FileNames());
    }

    [Fact]
    public async Task Reset_in_an_empty_folder_creates_the_pairing_the_next_start_loads()
    {
        using var data = new TempFolder();
        var protector = new FakePairingProtector();

        await StoredPairingReset.RunAsync(data.Path, protector, files: null, CancellationToken.None);

        Assert.Equal([PairingFile.Name], data.FileNames());
        Assert.NotNull(await ReadAsync(data, protector));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_reset_that_cannot_write_throws_the_fixed_text_and_keeps_the_old_file(bool writesFail, bool protectorFails)
    {
        using var data = new TempFolder();
        var protector = new FakePairingProtector();
        var files = new InMemoryPairingFiles();
        var path = PairingFile.PathIn(data.Path);
        var old = PairingHarness.FileFor(TestTokens.Example, protector);
        files.Put(path, old);
        files.WritesFail = writesFail;
        protector.Fails = protectorFails;

        var exception = await Assert.ThrowsAsync<IOException>(
            () => StoredPairingReset.RunAsync(data.Path, protector, files, CancellationToken.None));

        Assert.Equal("The pairing could not be saved.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(old, files.Get(path));
        Assert.Equal(0, files.Writes);
    }

    // A data folder that does not exist holds no pairing: nothing to replace, and nothing is created.
    [Fact]
    public async Task A_data_folder_that_does_not_exist_is_left_alone()
    {
        using var parent = new TempFolder();
        var missing = parent.Combine("no-such-folder");
        var files = new InMemoryPairingFiles();
        var protector = new FakePairingProtector();

        await StoredPairingReset.RunAsync(missing, protector, files, CancellationToken.None);
        await StoredPairingReset.RunAsync(missing, protector, files: null, CancellationToken.None);
        var payload = await StoredPairingPayload.ReadAsync(missing, Endpoint, protector, files: null, CancellationToken.None);

        Assert.Null(payload);
        Assert.False(Directory.Exists(missing));
        Assert.Empty(parent.FileNames());
        Assert.Empty(files.Paths);
        Assert.Equal(0, protector.ProtectCalls);
    }

    [Fact]
    public async Task The_stored_export_reads_the_payload_for_the_canonical_endpoint_and_never_writes()
    {
        using var data = new TempFolder();
        var protector = new FakePairingProtector();
        var files = new InMemoryPairingFiles();
        var path = PairingFile.PathIn(data.Path);

        // No file: null, not an error.
        Assert.Null(await StoredPairingPayload.ReadAsync(data.Path, Endpoint, protector, files, CancellationToken.None));

        var contents = PairingHarness.FileFor(TestTokens.Example, protector);
        files.Put(path, contents);
        var payload = await StoredPairingPayload.ReadAsync(
            data.Path, "https://PC.example.test:10000/livecue", protector, files, CancellationToken.None);

        Assert.Equal(
            "{\"endpoint\":\"https://pc.example.test:10000/livecue/\",\"token\":\"" + TestTokens.Example + "\"}",
            payload);
        Assert.Equal(contents, files.Get(path));
        Assert.Equal(0, files.Writes);
        Assert.Equal([path], files.Paths);
        Assert.Empty(data.FileNames());
    }

    [Fact]
    public async Task The_stored_export_refuses_a_bad_endpoint_and_an_unreadable_file_with_fixed_texts()
    {
        using var data = new TempFolder();
        var protector = new FakePairingProtector();
        var files = new InMemoryPairingFiles();
        var path = PairingFile.PathIn(data.Path);
        files.Put(path, PairingHarness.FileFor(TestTokens.Example, protector));

        await Assert.ThrowsAsync<ArgumentException>(
            () => StoredPairingPayload.ReadAsync(data.Path, "http://pc.example.test/", protector, files, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => StoredPairingExport.ReadPayloadAsync(data.Path, "https://pc.example.test/?x=1", CancellationToken.None));

        // Another Windows account's file, as the fake protector plays it.
        protector.Fails = true;
        var exception = await Assert.ThrowsAsync<IOException>(
            () => StoredPairingPayload.ReadAsync(data.Path, Endpoint, protector, files, CancellationToken.None));

        Assert.Equal("The saved pairing could not be read.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(TestTokens.Example, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, files.Writes);
    }

    [Fact]
    public async Task A_cancelled_call_does_nothing()
    {
        using var data = new TempFolder();
        var cancelled = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => StoredPairingReset.RunAsync(data.Path, new FakePairingProtector(), files: null, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => StoredPairingPayload.ReadAsync(data.Path, Endpoint, new FakePairingProtector(), files: null, cancelled));

        Assert.Empty(data.FileNames());
    }

    private static Task<string?> ReadAsync(TempFolder data, FakePairingProtector protector) =>
        StoredPairingPayload.ReadAsync(data.Path, Endpoint, protector, files: null, CancellationToken.None);
}
