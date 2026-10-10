using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// Protocol P2-148 through a real start: the relay validates its own advertised endpoint with the
/// grammar of protocol 4.3 before it starts. An endpoint that fails is a start fault, not a QR; one that
/// passes is advertised in canonical form everywhere.
/// </summary>
public sealed class PairingStartTests
{
    [Theory]

    // Outside the grammar: the reject cases of the statement, and a text that ends in a line break.
    [InlineData("http://pc.example.test:11443/", null)]
    [InlineData("HTTPS://pc.example.test:11443/", null)]
    [InlineData("https://pc.example.test:11443/?x=1", null)]
    [InlineData("https://pc.example.test:11443//", null)]
    [InlineData("https://pc.example.test:11443/../", null)]
    [InlineData("https://pc.example.test:11443/a/b/c/d/e", null)]
    [InlineData("https://pc.example.test:11443/\n", null)]
    [InlineData("pc.example.test", null)]
    [InlineData("", null)]

    // Inside it: the canonical form is what is advertised.
    [InlineData("https://pc.example.test:11443", "https://pc.example.test:11443/")]
    [InlineData("https://pc.example.test:11443/", "https://pc.example.test:11443/")]
    [InlineData("https://PC.Example.TEST:10000/livecue", "https://pc.example.test:10000/livecue/")]
    public async Task A_bad_endpoint_is_a_start_fault_and_a_missing_slash_is_added(string endpoint, string? advertised)
    {
        await using var host = RelayTestHost.Create(options => options.Endpoint = endpoint);

        if (advertised is null)
        {
            var exception = await Assert.ThrowsAsync<RelayStartException>(
                () => host.Relay.StartAsync(CancellationToken.None));
            await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(RelayFaultCode.InvalidEndpoint, exception.Code);
            Assert.Equal("The advertised endpoint is not valid.", exception.Message);
            Assert.Equal(RelayStatus.Faulted, host.Relay.Status);
            Assert.Equal(0, host.Relay.BoundPort);
            Assert.Equal(RelayFaultCode.InvalidEndpoint, Assert.Single(host.Events.OfType<RelayFaulted>()).Code);

            // A fault, not a QR: no pairing was created for an endpoint that cannot be advertised.
            Assert.Empty(host.Events.OfType<PairingShown>());
            Assert.Empty(host.PairingFiles.Paths);
            Assert.Equal(0, host.Protector.ProtectCalls);
            return;
        }

        await host.StartRelayAsync();
        var started = await host.Events.WaitForAsync<RelayStarted>();
        var qr = await host.Relay.ShowPairingAsync(CancellationToken.None);
        var shown = await host.Events.WaitForAsync<PairingShown>();
        await host.Events.WaitForAsync<SelfCheckCompleted>();
        var payload = await ((IPairingExport)host.Relay).GetPairingPayloadAsync(CancellationToken.None);

        Assert.Equal(advertised, started.Endpoint);
        Assert.Equal(advertised, qr.Endpoint);
        Assert.Equal(advertised, shown.Endpoint);
        Assert.Equal("{\"endpoint\":\"" + advertised + "\",\"token\":\"" + host.Token + "\"}", payload);

        // The self-check goes to the canonical endpoint too: E + "v1/health", by text concatenation.
        var call = Assert.Single(host.SelfCheck.Calls);
        Assert.Equal(advertised + "v1/health", call.HealthUrl.OriginalString);
        Assert.Equal(advertised + "v1/health", call.HealthUrl.AbsoluteUri);
    }
}
