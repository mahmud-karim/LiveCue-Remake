using LiveCue.Relay.Diagnostics;
using LiveCue.Relay.Pairing;

namespace LiveCue.Relay.Tests;

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
    public void The_verifier_accepts_only_its_token()
    {
        var token = PairingToken.Create();
        var verifier = HashedTokenVerifier.For(token);

        Assert.True(verifier.IsAccepted(token));
        Assert.False(verifier.IsAccepted(PairingToken.Create()));
        Assert.False(verifier.IsAccepted(token.AsSpan(0, 42)));
        Assert.False(verifier.IsAccepted(string.Empty));
    }

    [Fact]
    public void The_event_channel_drops_the_oldest_event_when_full_and_counts_the_drop()
    {
        var channel = new EventChannel();
        var start = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

        for (var index = 0; index <= EventChannel.Capacity; index++)
        {
            channel.Raise(new PhoneSeen(start.AddSeconds(index)));
        }

        Assert.Equal(1, channel.Dropped);
        Assert.True(channel.Reader.TryRead(out var oldestKept));
        Assert.Equal(new PhoneSeen(start.AddSeconds(1)), oldestKept);
    }
}
