using LiveCue.Relay.Authentication;
using Microsoft.Extensions.Primitives;

namespace LiveCue.Relay.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("Bearer abc", "abc")]
    [InlineData("bearer abc", "abc")]
    [InlineData("Bearer   abc", "abc")]
    [InlineData("Bearer abc def", "abc def")]
    public void A_bearer_header_yields_its_token(string header, string expected)
    {
        Assert.True(BearerToken.TryRead(new StringValues(header), out var token));
        Assert.Equal(expected, token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearerabc")]
    [InlineData("Basic abc")]
    [InlineData(" Bearer abc")]
    public void Anything_else_yields_nothing(string header)
    {
        Assert.False(BearerToken.TryRead(new StringValues(header), out var token));
        Assert.Null(token);
    }

    [Fact]
    public void A_missing_or_repeated_header_yields_nothing()
    {
        Assert.False(BearerToken.TryRead(StringValues.Empty, out _));
        Assert.False(BearerToken.TryRead(new StringValues(["Bearer abc", "Bearer abc"]), out _));
    }

    [Fact]
    public void The_shared_token_authenticator_accepts_only_its_token()
    {
        var authenticator = new SharedTokenAuthenticator("correct horse");

        Assert.True(authenticator.IsAccepted("correct horse"));
        Assert.False(authenticator.IsAccepted("correct horse "));
        Assert.False(authenticator.IsAccepted("Correct horse"));
        Assert.False(authenticator.IsAccepted(string.Empty));
    }

    [Fact]
    public void Generated_tokens_are_43_base64url_characters_and_differ()
    {
        var first = SharedTokenAuthenticator.CreateToken();
        var second = SharedTokenAuthenticator.CreateToken();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_blank_token_cannot_be_configured()
    {
        Assert.Throws<ArgumentException>(() => new SharedTokenAuthenticator(" "));
    }
}
