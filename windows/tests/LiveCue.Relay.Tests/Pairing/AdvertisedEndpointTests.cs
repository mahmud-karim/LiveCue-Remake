using LiveCue.Relay.Pairing;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>Protocol 4.3: the endpoint grammar and its canonical form (statement P2-148).</summary>
public sealed class AdvertisedEndpointTests
{
    /// <summary>One row per text: its canonical form, or null when the grammar refuses it.</summary>
    [Theory]

    // Accepted, with the canonical form: host in lower case, the port kept, exactly one trailing slash.
    [InlineData("https://pc.example.test", "https://pc.example.test/")]
    [InlineData("https://pc.example.test/", "https://pc.example.test/")]
    [InlineData("https://PC.Example.TEST:11443", "https://pc.example.test:11443/")]
    [InlineData("https://pc.example.test:10000/livecue", "https://pc.example.test:10000/livecue/")]
    [InlineData("https://pc.example.test:10000/livecue/", "https://pc.example.test:10000/livecue/")]
    [InlineData("https://pc.example.test/a/B/c.d/e_f~g", "https://pc.example.test/a/B/c.d/e_f~g/")]
    [InlineData("https://pc.example.test/a/b/c/d/", "https://pc.example.test/a/b/c/d/")]
    [InlineData("https://pc.example.test/...", "https://pc.example.test/.../")]
    [InlineData("https://a:1", "https://a:1/")]
    [InlineData("https://a-b.c:65535/", "https://a-b.c:65535/")]
    [InlineData("https://pc.example.test:443/", "https://pc.example.test:443/")]
    [InlineData("https://127.0.0.1:8443/", "https://127.0.0.1:8443/")]

    // The reject cases of P2-148.
    [InlineData("http://pc.example.test/", null)]
    [InlineData("HTTPS://pc.example.test/", null)]
    [InlineData("Https://pc.example.test/", null)]
    [InlineData("https://pc.example.test/?q=1", null)]
    [InlineData("https://pc.example.test?q=1", null)]
    [InlineData("https://pc.example.test//", null)]
    [InlineData("https://pc.example.test/a//b", null)]
    [InlineData("https://pc.example.test/../", null)]
    [InlineData("https://pc.example.test/a/../b", null)]
    [InlineData("https://pc.example.test/a/b/c/d/e", null)]
    [InlineData("https://pc.example.test/a/b/c/d/e/", null)]

    // Everything else the grammar leaves out.
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("pc.example.test", null)]
    [InlineData("https://", null)]
    [InlineData("https:///path", null)]
    [InlineData("https://user@pc.example.test/", null)]
    [InlineData("https://user:password@pc.example.test/", null)]
    [InlineData("https://pc.example.test/#top", null)]
    [InlineData("https://pc.example.test/a%20b", null)]
    [InlineData("https://pc.example.test/a b", null)]
    [InlineData("https://pc.example.test/./", null)]
    [InlineData("https://pc.example.test/a/.", null)]
    [InlineData("https://pc.example.test/a\\b", null)]
    [InlineData("https://pc.example.test:0/", null)]
    [InlineData("https://pc.example.test:080/", null)]
    [InlineData("https://pc.example.test:65536/", null)]
    [InlineData("https://pc.example.test:99999/", null)]
    [InlineData("https://pc.example.test:123456/", null)]
    [InlineData("https://pc.example.test:/", null)]
    [InlineData("https://pc.example.test:12a/", null)]
    [InlineData("https://pc.example.test:+80/", null)]
    [InlineData("https://pc.example.test:1:2/", null)]
    [InlineData("https://-pc.example.test/", null)]
    [InlineData("https://pc-.example.test/", null)]
    [InlineData("https://pc..example.test/", null)]
    [InlineData("https://.pc.example.test/", null)]
    [InlineData("https://pc.example.test./", null)]
    [InlineData("https://pc_name.example.test/", null)]
    [InlineData("https://[::1]/", null)]
    [InlineData("https://pc.example.test /", null)]
    [InlineData(" https://pc.example.test/", null)]
    public void Grammar_cases(string? text, string? expected)
    {
        var accepted = AdvertisedEndpoint.TryCanonicalize(text, out var canonical);

        Assert.Equal(expected is not null, accepted);
        Assert.Equal(expected, canonical);
        if (expected is not null)
        {
            // The canonical form is a fixed point, and is recognised as canonical.
            Assert.True(AdvertisedEndpoint.IsCanonical(canonical));
            Assert.True(AdvertisedEndpoint.TryCanonicalize(canonical, out var again));
            Assert.Equal(canonical, again);
        }

        Assert.Equal(expected is not null && expected == text, AdvertisedEndpoint.IsCanonical(text));
    }

    // Test plan ST-13: the grammar is matched against the whole text.
    [Fact]
    public void An_endpoint_with_a_trailing_line_break_fails()
    {
        string[] endings =
        [
            "\n", "\r\n", "\r", "\t", " ", "\0",
            ((char)0x0085).ToString(), ((char)0x2028).ToString(), ((char)0x2029).ToString(),
        ];
        string[] endpoints =
        [
            "https://pc.example.test",
            "https://pc.example.test/",
            "https://pc.example.test:11443",
            "https://pc.example.test:11443/",
            "https://pc.example.test:10000/livecue",
            "https://pc.example.test:10000/livecue/",
        ];

        foreach (var endpoint in endpoints)
        {
            Assert.True(AdvertisedEndpoint.TryCanonicalize(endpoint, out _));
            foreach (var ending in endings)
            {
                Assert.False(AdvertisedEndpoint.TryCanonicalize(endpoint + ending, out var canonical));
                Assert.Null(canonical);
                Assert.False(AdvertisedEndpoint.IsCanonical(endpoint + ending));
                Assert.False(AdvertisedEndpoint.TryCanonicalize(ending + endpoint, out _));
            }
        }
    }

    [Fact]
    public void Lengths_are_limited()
    {
        var label63 = new string('a', 63);
        var segment64 = new string('s', 64);

        Assert.True(AdvertisedEndpoint.TryCanonicalize($"https://{label63}.test/{segment64}", out _));
        Assert.False(AdvertisedEndpoint.TryCanonicalize($"https://{label63}a.test/", out _));
        Assert.False(AdvertisedEndpoint.TryCanonicalize($"https://a.test/{segment64}s", out _));

        // Three labels of 63 and one of 61 are 253 characters; one more is too many.
        var host253 = string.Join('.', label63, label63, label63, new string('a', 61));
        Assert.Equal(253, host253.Length);
        Assert.True(AdvertisedEndpoint.TryCanonicalize($"https://{host253}/", out _));
        Assert.False(AdvertisedEndpoint.TryCanonicalize($"https://{host253}a/", out _));
    }
}
