using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>Token shape, generation, hashing and the constant-time comparison (protocol 4.1).</summary>
public sealed class PairingTokenTests
{
    [Fact]
    public void Generated_tokens_are_43_base64url_characters_and_differ()
    {
        var first = NewToken();
        var second = NewToken();

        Assert.Matches(@"\A[A-Za-z0-9_-]{43}\z", first);
        Assert.True(PairingToken.HasValidShape(first));
        Assert.True(PairingToken.HasValidShape(Encoding.ASCII.GetBytes(first)));
        Assert.NotEqual(first, second);
        Assert.Throws<ArgumentException>(() => PairingToken.Generate(new byte[42]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt=")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt+")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt ")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt\n")]
    [InlineData("ttttttttttttttttttttttttttttttttttttttttttt\n")]
    [InlineData("ttttttttttttttttttttttttttttttttttttttttttt\r\n")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt\u2028")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt\u00e9")]
    [InlineData("ttttttttttttttttttttttttttttttttttttttttttttt")]
    public void Anything_else_does_not_have_the_token_shape(string candidate)
    {
        Assert.False(PairingToken.HasValidShape(candidate));
        Assert.False(PairingToken.HasValidShape(Encoding.UTF8.GetBytes(candidate)));
        Assert.False(PairingToken.Matches(candidate, new byte[PairingToken.HashLength]));
    }

    // Protocol P2-140.
    [Fact]
    public void The_hash_verifies_the_token_and_does_not_contain_it()
    {
        var secret = NewToken();
        var wrong = NewToken();
        var hash = HashOf(secret);
        var hashText = PairingToken.HashText(hash);

        Assert.True(PairingToken.Matches(secret, hash));
        Assert.False(PairingToken.Matches(wrong, hash));
        Assert.False(PairingToken.Matches(TestTokens.WellFormedUnknown, hash));
        Assert.False(PairingToken.Matches(secret.AsSpan(0, 42), hash));
        Assert.False(PairingToken.Matches(string.Empty, hash));
        Assert.False(PairingToken.Matches(secret, hash.AsSpan(0, 31)));

        Assert.Equal(PairingToken.HashLength, hash.Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), hash);
        Assert.Matches(@"\A[a-f0-9]{64}\z", hashText);
        Assert.DoesNotContain(secret, hashText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, Convert.ToBase64String(hash), StringComparison.Ordinal);
    }

    // Test plan S-32.
    [Fact]
    public void Verify_uses_fixed_time_comparison_and_does_not_allocate()
    {
        using var harness = new PairingHarness();
        Assert.True(Load(harness.Service));
        var accepted = TokenOf(harness.Service);
        var unknown = TestTokens.WellFormedUnknown;

        // Warm up, so that nothing is compiled or initialised inside the measured part.
        Assert.True(harness.Service.TryVerify(accepted, out _));
        Assert.False(harness.Service.TryVerify(unknown, out _));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var accepts = 0;
        for (var round = 0; round < 1_000; round++)
        {
            accepts += harness.Service.TryVerify(accepted, out _) ? 1 : 0;
            accepts += harness.Service.TryVerify(unknown, out _) ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1_000, accepts);
        Assert.Equal(0, allocated);

        // The comparison is the framework's fixed-time one, and nothing else compares the hash.
        var calls = CalledMethods(typeof(PairingToken).GetMethod(nameof(PairingToken.Matches))!);
        Assert.Contains(calls, method =>
            method.DeclaringType == typeof(CryptographicOperations)
            && method.Name == nameof(CryptographicOperations.FixedTimeEquals));
        Assert.DoesNotContain(calls, method => method.Name.Contains("SequenceEqual", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wrong_hash_is_refused_wherever_it_differs()
    {
        var token = NewToken();
        var hash = HashOf(token);

        for (var index = 0; index < hash.Length; index++)
        {
            var other = (byte[])hash.Clone();
            other[index] ^= 0x01;
            Assert.False(PairingToken.Matches(token, other));
        }
    }

    [Fact]
    public void The_hash_text_is_a_whole_text_match_of_64_lower_case_hex_characters()
    {
        var hash = HashOf(NewToken());
        var text = PairingToken.HashText(hash);
        var parsed = new byte[PairingToken.HashLength];

        Assert.True(PairingToken.TryParseHashText(text, parsed));
        Assert.Equal(hash, parsed);
        Assert.False(PairingToken.TryParseHashText("A" + text[1..], parsed));
        Assert.False(PairingToken.TryParseHashText(text + "\n", parsed));
        Assert.False(PairingToken.TryParseHashText(text + "\r\n", parsed));
        Assert.False(PairingToken.TryParseHashText(text + "\u2028", parsed));
        Assert.False(PairingToken.TryParseHashText(text.AsSpan(0, 63), parsed));
        Assert.False(PairingToken.TryParseHashText(text[..63] + "g", parsed));
        Assert.False(PairingToken.TryParseHashText(text[..63] + "\n", parsed));
        Assert.False(PairingToken.TryParseHashText(string.Empty, parsed));
    }

    private static string NewToken()
    {
        var bytes = new byte[PairingToken.Length];
        PairingToken.Generate(bytes);
        return Encoding.ASCII.GetString(bytes);
    }

    private static byte[] HashOf(string token)
    {
        var hash = new byte[PairingToken.HashLength];
        PairingToken.Hash(Encoding.ASCII.GetBytes(token), hash);
        return hash;
    }

    private static bool Load(PairingService service) =>
        service.LoadOrCreateAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static string TokenOf(PairingService service) =>
        PairingHarness.TokenIn(service.GetPayloadAsync(CancellationToken.None).GetAwaiter().GetResult());

    /// <summary>
    /// The methods a method calls, read from its instructions: every position that could be a call
    /// instruction is tried, and what does not resolve to a method is skipped.
    /// </summary>
    private static List<MethodBase> CalledMethods(MethodInfo method)
    {
        const byte Call = 0x28;
        const byte CallVirtual = 0x6F;
        var instructions = method.GetMethodBody()!.GetILAsByteArray()!;
        var module = method.Module;
        var found = new List<MethodBase>();
        for (var index = 0; index + 4 < instructions.Length; index++)
        {
            if (instructions[index] is not (Call or CallVirtual))
            {
                continue;
            }

            try
            {
                if (module.ResolveMethod(BitConverter.ToInt32(instructions, index + 1)) is { } called)
                {
                    found.Add(called);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException)
            {
                // These bytes were not a call instruction.
            }
        }

        return found;
    }
}
