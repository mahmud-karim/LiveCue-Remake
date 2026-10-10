using LiveCue.Relay.Pairing;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// Token shape, hashing and comparison (protocol 4.1). Carried over from the spike; work package WP-W02
/// owns this file.
/// </summary>
public sealed class PairingTokenTests
{
    [Fact]
    public void Generated_tokens_are_43_base64url_characters_and_differ()
    {
        var first = PairingToken.Create();
        var second = PairingToken.Create();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
        Assert.True(PairingToken.HasValidShape(first));
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt=")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt+")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttt ")]
    [InlineData("ttttttttttttttttttttttttttttttttttttttttttttt")]
    public void Anything_else_does_not_have_the_token_shape(string candidate)
    {
        Assert.False(PairingToken.HasValidShape(candidate));
    }

    [Fact]
    public void The_hash_verifies_the_token_and_does_not_contain_it()
    {
        var token = PairingToken.Create();
        var hash = PairingToken.Hash(token);

        Assert.Equal(PairingToken.HashLength, hash.Length);
        Assert.True(PairingToken.Matches(token, hash));
        Assert.False(PairingToken.Matches(PairingToken.Create(), hash));
        Assert.False(PairingToken.Matches(token.AsSpan(0, 42), hash));
        Assert.False(PairingToken.Matches(string.Empty, hash));
        Assert.False(PairingToken.Matches(token, hash.AsSpan(0, 31)));
        Assert.DoesNotContain(token, Convert.ToHexString(hash), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_payload_is_the_exact_text_of_the_protocol()
    {
        var payload = PairingPayload.Build("https://pc.example.test:11443/", new string('A', 43));

        Assert.Equal(
            """{"endpoint":"https://pc.example.test:11443/","token":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""",
            payload);
    }
}
