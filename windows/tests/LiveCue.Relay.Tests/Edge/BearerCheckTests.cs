using System.Text;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Steps E-1 and E-2 as pure functions (W-design 4.4, P-2 and P-3), and test plan ST-13: the token shape
/// C-02 is matched against the whole text.
/// </summary>
public sealed class BearerCheckTests
{
    private static readonly string Token = TestTokens.Example;

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(Wire.LineSeparator)]
    public async Task A_token_with_a_trailing_line_break_is_the_fixed_401(string lineBreak)
    {
        // The pure function: 43 valid characters and then the break never match.
        Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer " + Token + lineBreak), out var token));
        Assert.True(token.IsEmpty);

        // Through a real listener, with the token the relay accepts. A line separator travels inside the
        // header value and is the fixed 401. A raw line feed cannot be inside a header value at all: the
        // server core ends the line there, and what follows is its business (a bare 400, or a request
        // whose Authorization value is the token alone). It is never a request that passed E-2 with the
        // break in the value.
        await using var host = await RelayTestHost.StartAsync();
        using var raw = await host.OpenRawAsync();
        await raw.SendAsync(Encoding.UTF8.GetBytes(Wire.Head(
            host.Port,
            "GET /v1/health HTTP/1.1",
            ("Authorization", TestTokens.Bearer(host.Token) + lineBreak + "X-After: 1"),
            ("X-LiveCue-Protocol", "2"))));
        var response = await Wire.ReadRawAsync(raw);

        Assert.NotNull(response);
        if (lineBreak == Wire.LineSeparator)
        {
            Wire.AssertFixed401(Wire.WithoutDate(response));
        }
        else
        {
            Assert.Contains(Wire.StatusLine(response), (string[])["HTTP/1.1 200 OK", "HTTP/1.1 400 Bad Request"]);
        }
    }

    [Fact]
    public void Exactly_one_value_of_the_exact_shape_is_read()
    {
        Assert.True(BearerCheck.TryReadToken(new StringValues("Bearer " + Token), out var token));
        Assert.Equal(Token, token.ToString());
        Assert.True(BearerCheck.TryReadToken(new StringValues("Bearer " + new string('-', 43)), out _));
        Assert.True(BearerCheck.TryReadToken(new StringValues("Bearer " + new string('_', 43)), out _));

        Assert.False(BearerCheck.TryReadToken(StringValues.Empty, out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues(string.Empty), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues(["Bearer " + Token, "Bearer " + Token]), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("bearer " + Token), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("BEARER " + Token), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer  " + Token), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer\t" + Token), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer" + Token), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues(Token), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer " + Token[..42]), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer " + Token + "A"), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues(" Bearer " + Token), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer " + Token + " "), out _));
        Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer " + Token + "\0"), out _));
        // Outside the alphabet: base64 and URL characters, a Latin letter with an accent, a Cyrillic letter.
        foreach (var outside in (char[])['=', '+', '/', '.', ' ', '%', (char)0xE9, (char)0x430])
        {
            Assert.False(BearerCheck.TryReadToken(new StringValues("Bearer " + Token[..42] + outside), out _));
        }
    }

    [Fact]
    public void The_two_checks_allocate_nothing()
    {
        // Protocol 5.5: a refused request is counted "without allocating and without any other work".
        // These two functions are everything the edge runs before the token is compared.
        IHeaderDictionary refused = new HeaderDictionary { ["Origin"] = "https://bad.test", ["Authorization"] = "Bearer " + Token };
        IHeaderDictionary malformed = new HeaderDictionary { ["Authorization"] = "bearer " + Token };
        IHeaderDictionary wellFormed = new HeaderDictionary { ["Authorization"] = "Bearer " + Token };
        var found = 0;

        void Run()
        {
            for (var index = 0; index < 1_000; index++)
            {
                found += BearerCheck.FindRefusedHeader(refused, ConnectionMode.Private) == RefusedHeader.Browser ? 1 : 0;
                found += BearerCheck.FindRefusedHeader(malformed, ConnectionMode.Private) == RefusedHeader.None ? 1 : 0;
                found += BearerCheck.TryReadToken(malformed.Authorization, out _) ? 0 : 1;
                found += BearerCheck.TryReadToken(wellFormed.Authorization, out var token) && token.Length == 43 ? 1 : 0;
            }
        }

        Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(8_000, found);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_refused_header_counts_whatever_its_value_and_the_funnel_header_is_looked_at_first()
    {
        static HeaderDictionary Headers(params string[] names)
        {
            var headers = new HeaderDictionary();
            foreach (var name in names)
            {
                headers[name] = string.Empty;
            }

            return headers;
        }

        Assert.Equal(RefusedHeader.None, BearerCheck.FindRefusedHeader(Headers("Cookie", "X-Forwarded-For"), ConnectionMode.Private));
        Assert.Equal(RefusedHeader.Browser, BearerCheck.FindRefusedHeader(Headers("Origin"), ConnectionMode.Private));
        Assert.Equal(RefusedHeader.Browser, BearerCheck.FindRefusedHeader(Headers("origin"), ConnectionMode.Public));
        Assert.Equal(RefusedHeader.Browser, BearerCheck.FindRefusedHeader(Headers("Sec-Fetch-Site"), ConnectionMode.Public));
        Assert.Equal(
            RefusedHeader.FunnelInPrivateMode,
            BearerCheck.FindRefusedHeader(Headers("Tailscale-Funnel-Request"), ConnectionMode.Private));
        Assert.Equal(
            RefusedHeader.FunnelInPrivateMode,
            BearerCheck.FindRefusedHeader(Headers("Origin", "Tailscale-Funnel-Request"), ConnectionMode.Private));

        // In Public mode the Funnel header is not a reason; a browser header beside it still is.
        Assert.Equal(RefusedHeader.None, BearerCheck.FindRefusedHeader(Headers("Tailscale-Funnel-Request"), ConnectionMode.Public));
        Assert.Equal(
            RefusedHeader.Browser,
            BearerCheck.FindRefusedHeader(Headers("Origin", "Tailscale-Funnel-Request"), ConnectionMode.Public));
    }
}
