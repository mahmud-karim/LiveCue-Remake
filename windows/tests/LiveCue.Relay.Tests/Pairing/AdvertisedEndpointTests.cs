using LiveCue.Relay.Pairing;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// Protocol 4.3: the endpoint grammar and its canonical form. Carried over from the spike; work package
/// WP-W02 owns this file.
/// </summary>
public sealed class AdvertisedEndpointTests
{
    [Theory]
    [InlineData("https://pc.example.test", "https://pc.example.test/")]
    [InlineData("https://pc.example.test/", "https://pc.example.test/")]
    [InlineData("https://PC.Example.TEST:11443", "https://pc.example.test:11443/")]
    [InlineData("https://pc.example.test:10000/livecue", "https://pc.example.test:10000/livecue/")]
    [InlineData("https://pc.example.test:10000/livecue/", "https://pc.example.test:10000/livecue/")]
    [InlineData("https://pc.example.test/a/B/c.d/e_f~g", "https://pc.example.test/a/B/c.d/e_f~g/")]
    [InlineData("https://a:1", "https://a:1/")]
    [InlineData("https://a-b.c:65535/", "https://a-b.c:65535/")]
    public void A_valid_endpoint_has_one_canonical_form(string text, string expected)
    {
        Assert.True(AdvertisedEndpoint.TryCanonicalize(text, out var canonical));
        Assert.Equal(expected, canonical);
        Assert.True(AdvertisedEndpoint.IsCanonical(canonical));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pc.example.test")]
    [InlineData("http://pc.example.test/")]
    [InlineData("HTTPS://pc.example.test/")]
    [InlineData("https://")]
    [InlineData("https:///path")]
    [InlineData("https://user@pc.example.test/")]
    [InlineData("https://pc.example.test/?q=1")]
    [InlineData("https://pc.example.test/#top")]
    [InlineData("https://pc.example.test/a%20b")]
    [InlineData("https://pc.example.test//")]
    [InlineData("https://pc.example.test/a//b")]
    [InlineData("https://pc.example.test/./")]
    [InlineData("https://pc.example.test/../")]
    [InlineData("https://pc.example.test/a/b/c/d/e")]
    [InlineData("https://pc.example.test:0/")]
    [InlineData("https://pc.example.test:080/")]
    [InlineData("https://pc.example.test:65536/")]
    [InlineData("https://pc.example.test:/")]
    [InlineData("https://pc.example.test:12a/")]
    [InlineData("https://-pc.example.test/")]
    [InlineData("https://pc-.example.test/")]
    [InlineData("https://pc..example.test/")]
    [InlineData("https://pc_name.example.test/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://pc.example.test /")]
    public void Anything_outside_the_grammar_is_refused(string? text)
    {
        Assert.False(AdvertisedEndpoint.TryCanonicalize(text, out var canonical));
        Assert.Null(canonical);
        Assert.False(AdvertisedEndpoint.IsCanonical(text));
    }

    [Fact]
    public void Lengths_are_limited()
    {
        var label63 = new string('a', 63);
        var segment64 = new string('s', 64);

        Assert.True(AdvertisedEndpoint.TryCanonicalize($"https://{label63}.test/{segment64}", out _));
        Assert.False(AdvertisedEndpoint.TryCanonicalize($"https://{label63}a.test/", out _));
        Assert.False(AdvertisedEndpoint.TryCanonicalize($"https://a.test/{segment64}s", out _));
        Assert.False(AdvertisedEndpoint.TryCanonicalize("https://" + string.Join('.', Enumerable.Repeat(label63, 4)) + "/", out _));
    }

    [Theory]
    [InlineData("https://pc.example.test")]
    [InlineData("https://PC.example.test/")]
    [InlineData("https://pc.example.test/livecue")]
    public void A_valid_endpoint_that_is_not_canonical_is_recognised_as_such(string text)
    {
        Assert.False(AdvertisedEndpoint.IsCanonical(text));
    }
}
