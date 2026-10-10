using System.Net;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>Protocol P2-007 (step E-5): the protocol header is exactly <c>2</c> on every HTTP route.</summary>
public sealed class ProtocolHeaderTests : IAsyncLifetime
{
    private RelayTestHost _host = null!;

    public async Task InitializeAsync() => _host = await RelayTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("")]
    [InlineData("2.0")]
    [InlineData("02")]
    [InlineData("2,2")]
    public async Task Absent_or_other_version_is_400_and_an_unknown_route_stays_404(string? protocol)
    {
        foreach (var route in Wire.HttpRoutes)
        {
            using var mismatch = await _host.SendAsync(_host.Request(
                new HttpMethod(route.Method), Wire.TargetOf(route), protocol: protocol, json: route.ReadsBody ? "{}" : null));

            await Wire.AssertErrorAsync(mismatch, 400, "PROTOCOL_MISMATCH");
        }

        using var notFound = await _host.SendAsync(_host.Request(HttpMethod.Get, "/v1/unknown", protocol: protocol));
        await _host.Events.SettleAsync();

        await Wire.AssertErrorAsync(notFound, 404, "NOT_FOUND");

        // The route's own work never began.
        Assert.Equal(0, _host.Catalog.Calls);
        Assert.Equal(0, _host.Lab.ProbeCalls);
        Assert.Empty(_host.Backend.Runs);
    }

    [Fact]
    public async Task A_repeated_header_is_a_mismatch_even_when_both_values_are_2()
    {
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(Wire.Head(
            _host.Port,
            "GET /v1/health HTTP/1.1",
            ("Authorization", TestTokens.Bearer(_host.Token)),
            ("X-LiveCue-Protocol", "2"),
            ("X-LiveCue-Protocol", "2")));

        var response = await raw.ReadResponseAsync();

        Assert.Equal(400, response!.Status);
        Assert.Equal(Wire.ProtocolMismatch, response.Body);
    }

    [Fact]
    public async Task The_exact_header_is_served()
    {
        using var response = await _host.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
