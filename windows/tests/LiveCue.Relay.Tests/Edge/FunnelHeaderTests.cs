using System.Net;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>Protocol P2-016 (step E-1, Private mode): a request that arrived through Funnel.</summary>
public sealed class FunnelHeaderTests
{
    [Theory]
    [InlineData("?1")]
    [InlineData("")]
    [InlineData("anything")]
    public async Task Private_refuses_and_public_serves_a_funnel_marked_request(string value)
    {
        await using var privateHost = await RelayTestHost.StartAsync(options => options.Mode = ConnectionMode.Private);
        await using var publicHost = await RelayTestHost.StartAsync(options => options.Mode = ConnectionMode.Public);

        var reference = await Wire.ExchangeAsync(privateHost.Port, Wire.Head(privateHost.Port, "GET /v1/health HTTP/1.1"));
        var refused = await Wire.ExchangeAsync(
            privateHost.Port, privateHost.RawHead("GET /v1/health HTTP/1.1", ("Tailscale-Funnel-Request", value)));
        using var raw = await publicHost.OpenRawAsync();
        await raw.SendAsync(publicHost.RawHead("GET /v1/health HTTP/1.1", ("Tailscale-Funnel-Request", value)));
        var served = await raw.ReadResponseAsync();
        await privateHost.Events.SettleAsync();
        await publicHost.Events.SettleAsync();

        // Valid credentials, and still the same 401 as a caller without a token gets.
        Wire.AssertFixed401(refused);
        Assert.Equal(reference, refused);
        Assert.Equal(0, privateHost.Events.Count<PhoneSeen>());

        Assert.Equal(200, served!.Status);
        Assert.Equal(1, publicHost.Events.Count<PhoneSeen>());
    }

    [Fact]
    public async Task Without_the_header_private_mode_serves()
    {
        await using var host = await RelayTestHost.StartAsync(options => options.Mode = ConnectionMode.Private);

        using var response = await host.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task From_outside_private_and_public_mode_answer_a_caller_without_the_token_alike()
    {
        await using var privateHost = await RelayTestHost.StartAsync(options => options.Mode = ConnectionMode.Private);
        await using var publicHost = await RelayTestHost.StartAsync(options => options.Mode = ConnectionMode.Public);

        var inPrivate = await Wire.ExchangeAsync(
            privateHost.Port, Wire.Head(privateHost.Port, "GET /v1/health HTTP/1.1", ("Tailscale-Funnel-Request", "?1")));
        var inPublic = await Wire.ExchangeAsync(
            publicHost.Port, Wire.Head(publicHost.Port, "GET /v1/health HTTP/1.1", ("Tailscale-Funnel-Request", "?1")));

        // The two requests differ in their Host header (the port); nothing in the two answers differs.
        Assert.Equal(inPrivate, inPublic);
    }
}
