using System.Text;
using System.Text.Json;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// <c>pairing.json</c>: what is written, what a load accepts, and that a file which fails a rule stops
/// the relay from starting and is never touched (protocol 4.4, architecture 3.5, W-design 4.7).
/// </summary>
public sealed class PairingStoreTests
{
    // Protocol P2-141.
    [Fact]
    public async Task Create_writes_a_hash_and_a_protected_copy_and_never_the_token()
    {
        using var harness = new PairingHarness(realFiles: true);
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1_500));

        var created = await harness.Service.LoadOrCreateAsync(CancellationToken.None);
        var token = await PairingHarness.TokenOfAsync(harness.Service);
        var bytes = await File.ReadAllBytesAsync(harness.PairingPath);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.True(created);
        Assert.Matches(@"\A[A-Za-z0-9_-]{43}\z", token);
        Assert.True(harness.Service.TryVerify(token, out _));

        // The exact text of architecture 3.5: no byte-order mark, two-space indent, line feeds, the four
        // members in their order, the time in UTC to the second.
        var tokenBytes = Encoding.ASCII.GetBytes(token);
        var protectedCopy = Convert.ToBase64String(new FakePairingProtector().Protect(tokenBytes));
        Assert.Equal(
            "{\n"
            + "  \"schemaVersion\": 1,\n"
            + "  \"tokenHash\": \"" + PairingHarness.HashTextOf(token) + "\",\n"
            + "  \"protectedToken\": \"" + protectedCopy + "\",\n"
            + "  \"createdAt\": \"2026-10-10T09:00:01Z\"\n"
            + "}\n",
            text);
        Assert.Equal((byte)'{', bytes[0]);

        // No member holds the token, and the text does not contain it in any plain form.
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(
            ["schemaVersion", "tokenHash", "protectedToken", "createdAt"],
            document.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.Matches(@"\A[a-f0-9]{64}\z", document.RootElement.GetProperty("tokenHash").GetString());
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(tokenBytes), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(tokenBytes), text, StringComparison.OrdinalIgnoreCase);

        // Nothing else was left in the folder: the pending file became the pairing file.
        Assert.Equal([PairingFile.Name], harness.Data.FileNames());
    }

    // Protocol P2-142.
    [Fact]
    public async Task Reload_returns_the_same_token_and_showing_twice_leaves_the_file_identical()
    {
        using var harness = new PairingHarness();
        Assert.True(await harness.Service.LoadOrCreateAsync(CancellationToken.None));
        var token = await PairingHarness.TokenOfAsync(harness.Service);
        var file = harness.ReadPairingFile();

        // The next start loads the file: nothing is created, and the same token keeps working.
        var restarted = harness.NewService();
        Assert.False(await restarted.LoadOrCreateAsync(CancellationToken.None));
        Assert.Equal(token, await PairingHarness.TokenOfAsync(restarted));
        Assert.True(restarted.TryVerify(token, out _));

        var writes = harness.Files.Writes;
        var decryptions = harness.Protector.UnprotectCalls;
        var first = await restarted.ShowAsync(CancellationToken.None);
        var second = await restarted.ShowAsync(CancellationToken.None);
        await harness.Events.WaitForAsync<SelfCheckCompleted>(count: 2);

        // The file is byte-identical, nothing was written, and each show decrypted once (WIN-20).
        Assert.Equal(file, harness.ReadPairingFile());
        Assert.Equal(writes, harness.Files.Writes);
        Assert.Equal(1, harness.Protector.ProtectCalls);
        Assert.Equal(decryptions + 2, harness.Protector.UnprotectCalls);
        Assert.Equal([harness.PairingPath], harness.Files.Paths);

        // What was shown is the text of protocol 4.2 for that token and the advertised endpoint.
        var expected = "{\"endpoint\":\"" + PairingHarness.Endpoint + "\",\"token\":\"" + token + "\"}";
        Assert.Equal(expected, await restarted.GetPayloadAsync(CancellationToken.None));
        Assert.Equal(QrMatrix.Encode(Encoding.UTF8.GetBytes(expected)), first.Modules);
        Assert.Equal(first.Modules, second.Modules);
        Assert.Equal(PairingHarness.Endpoint, first.Endpoint);

        // Showing never rotates: two "shown" events for the endpoint, no notice, and both checks ran.
        Assert.Equal(
            [PairingHarness.Endpoint, PairingHarness.Endpoint],
            harness.Events.OfType<PairingShown>().Select(shown => shown.Endpoint));
        Assert.Empty(harness.Events.OfType<Notice>());
        Assert.All(harness.SelfCheck.Calls, call => Assert.Equal(token, call.Token));
    }

    // Protocol P2-146, through a real start.
    [Theory]
    [InlineData("not json")]
    [InlineData("empty file")]
    [InlineData("an array")]
    [InlineData("a byte-order mark")]
    [InlineData("text after the object")]
    [InlineData("no version")]
    [InlineData("wrong version")]
    [InlineData("version as text")]
    [InlineData("no hash")]
    [InlineData("hash in upper case")]
    [InlineData("hash one character short")]
    [InlineData("hash with another character")]
    [InlineData("hash as a number")]
    [InlineData("hash with a broken escape")]
    [InlineData("hash of another token")]
    [InlineData("no protected copy")]
    [InlineData("protected copy is not base64")]
    [InlineData("protected copy is empty")]
    [InlineData("protected copy is not protected")]
    [InlineData("protected copy of 42 characters")]
    [InlineData("protected copy outside the alphabet")]
    [InlineData("oversized")]
    [InlineData("protector failure")]
    [InlineData("read failure")]
    public async Task An_unreadable_file_refuses_to_start_and_is_left_untouched(string problem)
    {
        await using var host = RelayTestHost.Create();
        var path = PairingFile.PathIn(host.Data.Path);
        var contents = Unreadable(problem, host.Protector);
        host.PairingFiles.Put(path, contents);
        host.Protector.Fails = problem == "protector failure";
        host.PairingFiles.ReadsFail = problem == "read failure";
        var protections = host.Protector.ProtectCalls;

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => host.Relay.StartAsync(CancellationToken.None));
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // The pairing-store fault, with its fixed text, and nothing listening.
        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, exception.Code);
        Assert.Equal("The saved pairing could not be read.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);
        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, Assert.Single(host.Events.OfType<RelayFaulted>()).Code);
        Assert.Empty(host.Events.OfType<RelayStarted>());

        // The file is what it was, nothing was written beside it, and no token was created.
        Assert.Equal(contents, host.PairingFiles.Get(path));
        Assert.Equal([path], host.PairingFiles.Paths);
        Assert.Equal(0, host.PairingFiles.Writes);
        Assert.Equal(protections, host.Protector.ProtectCalls);
    }

    // Test plan ST-13: the hash shape is matched against the whole text.
    [Theory]
    [InlineData(0x000A)]
    [InlineData(0x000D)]
    [InlineData(0x2028)]
    [InlineData(0x0020)]
    public async Task A_token_hash_with_a_trailing_line_break_is_unreadable(int ending)
    {
        await using var host = RelayTestHost.Create();
        var path = PairingFile.PathIn(host.Data.Path);
        var hash = PairingHarness.HashTextOf(TestTokens.Example);
        var valid = Encoding.UTF8.GetString(PairingHarness.FileFor(TestTokens.Example, host.Protector));
        var contents = Encoding.UTF8.GetBytes(
            valid.Replace("\"" + hash + "\"", JsonSerializer.Serialize(hash + (char)ending), StringComparison.Ordinal));
        Assert.NotEqual(valid, Encoding.UTF8.GetString(contents));
        host.PairingFiles.Put(path, contents);

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => host.Relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, exception.Code);
        Assert.Equal(contents, host.PairingFiles.Get(path));
        Assert.Equal(0, host.PairingFiles.Writes);
        Assert.False(PairingFile.TryRead(contents, new byte[PairingToken.HashLength], out _));
    }

    // The counterpart of the two tests above: the same files without their one defect do load.
    [Theory]
    [InlineData("as written")]
    [InlineData("compact, other order, unknown members")]
    [InlineData("exactly the largest size")]
    public async Task A_file_that_passes_every_rule_loads_whatever_its_layout(string layout)
    {
        await using var host = RelayTestHost.Create();
        var path = PairingFile.PathIn(host.Data.Path);
        var written = PairingHarness.FileFor(TestTokens.Example, host.Protector);
        var copy = Convert.ToBase64String(host.Protector.Protect(Encoding.ASCII.GetBytes(TestTokens.Example)));
        var contents = layout switch
        {
            "as written" => written,
            "exactly the largest size" =>
                [.. written, .. Enumerable.Repeat((byte)' ', ProtocolConstants.MaxStateFileBytes - written.Length)],
            _ => Encoding.UTF8.GetBytes(
                "{\"note\":{\"kept\":[1,2]},\"createdAt\":null,\"protectedToken\":\"" + copy + "\",\"tokenHash\":\""
                + PairingHarness.HashTextOf(TestTokens.Example) + "\",\"schemaVersion\":1}"),
        };
        host.PairingFiles.Put(path, contents);

        await host.StartRelayAsync();
        var started = await host.Events.WaitForAsync<RelayStarted>();

        Assert.False(started.PairingWasCreated);
        Assert.Equal(TestTokens.Example, host.Token);
        Assert.Equal(contents, host.PairingFiles.Get(path));
        Assert.Equal(0, host.PairingFiles.Writes);
    }

    // Step 4 of a start has one fault. A first pairing that cannot be protected or written is that
    // fault too, and leaves nothing behind.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_pairing_that_cannot_be_created_refuses_to_start(bool writesFail, bool protectorFails)
    {
        await using var host = RelayTestHost.Create();
        host.PairingFiles.WritesFail = writesFail;
        host.Protector.Fails = protectorFails;

        var exception = await Assert.ThrowsAsync<RelayStartException>(
            () => host.Relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.PairingStoreUnreadable, exception.Code);
        Assert.Null(exception.InnerException);
        Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
        Assert.Equal(0, host.Relay.BoundPort);
        Assert.Empty(host.PairingFiles.Paths);
    }

    // "Show QR" shows the pairing this relay accepts, or nothing: a file that changed under a running
    // relay is not turned into a QR that would pair no phone.
    [Theory]
    [InlineData("another pairing")]
    [InlineData("not a pairing")]
    [InlineData("gone")]
    public async Task A_show_refuses_a_file_that_is_no_longer_the_accepted_pairing(string change)
    {
        using var harness = new PairingHarness(realFiles: true);
        await harness.Service.LoadOrCreateAsync(CancellationToken.None);
        var token = await PairingHarness.TokenOfAsync(harness.Service);
        switch (change)
        {
            case "another pairing":
                await File.WriteAllBytesAsync(harness.PairingPath, PairingHarness.FileFor(TestTokens.Example, harness.Protector));
                break;
            case "not a pairing":
                await File.WriteAllTextAsync(harness.PairingPath, "{}");
                break;
            default:
                File.Delete(harness.PairingPath);
                break;
        }

        var contents = harness.ReadPairingFile();
        var shown = await Assert.ThrowsAsync<IOException>(() => harness.Service.ShowAsync(CancellationToken.None));
        var exported = await Assert.ThrowsAsync<IOException>(() => harness.Service.GetPayloadAsync(CancellationToken.None));

        Assert.Equal("The saved pairing could not be read.", shown.Message);
        Assert.Equal("The saved pairing could not be read.", exported.Message);
        Assert.Empty(harness.Events.OfType<PairingShown>());
        Assert.Empty(harness.SelfCheck.Calls);
        Assert.Equal(contents, harness.ReadPairingFile());

        // The accepted pairing is unchanged, and a reset is the way out.
        Assert.True(harness.Service.TryVerify(token, out _));
        await harness.Service.ResetAsync(CancellationToken.None);
        Assert.False(harness.Service.TryVerify(token, out _));
        Assert.NotEqual(token, await PairingHarness.TokenOfAsync(harness.Service));
    }

    [Fact]
    public async Task A_leftover_pending_file_is_ignored_at_load()
    {
        using var harness = new PairingHarness();
        var pending = harness.PairingPath + ".pending";
        harness.Files.Put(harness.PairingPath, PairingHarness.FileFor(TestTokens.Example, harness.Protector));
        harness.Files.Put(pending, "left by an interrupted write"u8.ToArray());

        Assert.False(await harness.Service.LoadOrCreateAsync(CancellationToken.None));

        Assert.True(harness.Service.TryVerify(TestTokens.Example, out _));
        Assert.Equal("left by an interrupted write"u8.ToArray(), harness.Files.Get(pending));
        Assert.Equal(0, harness.Files.Writes);
    }

    // Test plan S-33: a real relay on real files, through create, show, an interrupted reset and a reset.
    [Fact]
    public async Task No_file_in_the_data_folder_contains_the_token()
    {
        using var data = new TempFolder();
        var protector = new FakePairingProtector();
        var options = new RelayOptions(
            0, RelayTestHost.Endpoint, ConnectionMode.Private, data.Path, null, null, 47842, RelayTestHost.RelayVersion, false);
        await using var relay = RelayFactory.Create(options, new RelayDependencies
        {
            PairingProtector = protector,
            SelfCheckClient = new FakeSelfCheckClient(),
            Processes = new FakeProcessRunner(),
        });
        var path = PairingFile.PathIn(data.Path);
        var pending = path + ".pending";

        await relay.StartAsync(CancellationToken.None);
        var first = await TokenOfAsync(relay);
        await relay.ShowPairingAsync(CancellationToken.None);

        // While something else holds the pairing file, a reset can write its pending file and cannot
        // put it in place: the pending file stays behind, and the old pairing stays valid.
        string interrupted;
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => relay.ResetPairingAsync(CancellationToken.None));
            Assert.True(File.Exists(pending));
            interrupted = TokenProtectedIn(await File.ReadAllBytesAsync(pending), protector);
        }

        Assert.Equal(first, await TokenOfAsync(relay));
        AssertNoFileContains(data, first, interrupted);

        // The next reset overwrites the leftover and removes it.
        await relay.ResetPairingAsync(CancellationToken.None);
        var second = await TokenOfAsync(relay);
        await relay.StopAsync(CancellationToken.None);

        Assert.False(File.Exists(pending));
        Assert.Equal(3, new[] { first, interrupted, second }.Distinct().Count());
        Assert.Contains(PairingFile.Name, data.FileNames());
        AssertNoFileContains(data, first, interrupted, second);
    }

    private static async Task<string> TokenOfAsync(IRelay relay) =>
        PairingHarness.TokenIn(await ((IPairingExport)relay).GetPairingPayloadAsync(CancellationToken.None));

    /// <summary>The token whose protected copy a pairing file holds, as the fake protector reveals it.</summary>
    private static string TokenProtectedIn(byte[] file, FakePairingProtector protector)
    {
        using var document = JsonDocument.Parse(file);
        var copy = document.RootElement.GetProperty("protectedToken").GetBytesFromBase64();
        return Encoding.ASCII.GetString(protector.Unprotect(copy));
    }

    private static void AssertNoFileContains(TempFolder data, params string[] tokens)
    {
        var names = data.FileNames();
        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            var bytes = File.ReadAllBytes(data.Combine(name));
            foreach (var token in tokens)
            {
                Assert.Matches(@"\A[A-Za-z0-9_-]{43}\z", token);
                Assert.True(bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(token)) < 0, $"{name} contains a token.");
                Assert.True(bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(token)) < 0, $"{name} contains a token.");
            }
        }
    }

    /// <summary>A pairing file with exactly one defect, starting from one that loads.</summary>
    private static byte[] Unreadable(string problem, FakePairingProtector protector)
    {
        var token = TestTokens.Example;
        var hash = PairingHarness.HashTextOf(token);
        var written = PairingHarness.FileFor(token, protector);
        var valid = Encoding.UTF8.GetString(written);
        string Copy(string plaintext) => Convert.ToBase64String(protector.Protect(Encoding.ASCII.GetBytes(plaintext)));
        string With(string old, string replacement)
        {
            Assert.Contains(old, valid, StringComparison.Ordinal);
            return valid.Replace(old, replacement, StringComparison.Ordinal);
        }

        var text = problem switch
        {
            "not json" => "this is not json",
            "empty file" => string.Empty,
            "an array" => "[" + valid + "]",
            "text after the object" => valid + "{}",
            "no version" => With("  \"schemaVersion\": 1,\n", string.Empty),
            "wrong version" => With("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
            "version as text" => With("\"schemaVersion\": 1", "\"schemaVersion\": \"1\""),
            "no hash" => With("  \"tokenHash\": \"" + hash + "\",\n", string.Empty),
            "hash in upper case" => With(hash, hash.ToUpperInvariant()[..63] + "A"),
            "hash one character short" => With(hash, hash[..63]),
            "hash with another character" => With(hash, hash[..63] + "g"),
            "hash as a number" => With("\"" + hash + "\"", "1"),
            "hash with a broken escape" => With(hash, hash[..58] + (char)92 + "ud800"),
            "hash of another token" => With(hash, PairingHarness.HashTextOf(TestTokens.WellFormedUnknown)),
            "no protected copy" => With("  \"protectedToken\": \"" + Copy(token) + "\",\n", string.Empty),
            "protected copy is not base64" => With(Copy(token), "not*base64"),
            "protected copy is empty" => With(Copy(token), string.Empty),
            "protected copy is not protected" => With(Copy(token), Convert.ToBase64String(Encoding.ASCII.GetBytes(token))),
            "protected copy of 42 characters" => With(Copy(token), Copy(token[..42])),
            "protected copy outside the alphabet" => With(Copy(token), Copy(token[..42] + "+")),
            "oversized" => valid + new string(' ', ProtocolConstants.MaxStateFileBytes + 1 - written.Length),
            "protector failure" or "read failure" or "a byte-order mark" => valid,
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unknown case."),
        };

        var bytes = Encoding.UTF8.GetBytes(text);
        return problem == "a byte-order mark" ? [0xEF, 0xBB, 0xBF, .. bytes] : bytes;
    }
}
