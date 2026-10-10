using System.Net;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>Protocol P2-021, route 2: <c>POST /v1/pair/verify</c>.</summary>
public sealed class PairVerifyTests
{
    [Fact]
    public async Task Any_object_is_ok_also_while_paused_and_records_nothing()
    {
        await using var host = await RelayTestHost.StartAsync();
        var filesBefore = host.Data.FileNames();
        var writesBefore = host.PairingFiles.Writes;

        using var empty = await host.PostAsync("/v1/pair/verify", "{}");
        using var named = await host.PostAsync("/v1/pair/verify", """{"device":"x"}""");
        using var nested = await host.PostAsync("/v1/pair/verify", """{"token":"ignored","a":{"b":[1,2,3]}}""");
        await host.Relay.SetAcceptingAsync(false);
        using var paused = await host.PostAsync("/v1/pair/verify", "{}");
        await host.Events.SettleAsync();

        foreach (var response in (HttpResponseMessage[])[empty, named, nested, paused])
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("""{"ok":true}""", await response.Content.ReadAsStringAsync());
        }

        // Nothing is recorded: no event besides presence and the pause itself, no file, no other area.
        Assert.All(
            host.Events.Snapshot(),
            relayEvent => Assert.True(relayEvent is RelayStarted or PhoneSeen or AcceptingChanged));
        Assert.Equal(filesBefore, host.Data.FileNames());
        Assert.Equal(writesBefore, host.PairingFiles.Writes);
        Assert.Empty(host.Backend.Runs);
        Assert.Equal(0, host.Catalog.Calls);
        Assert.Equal(0, host.Lab.ProbeCalls);
    }

    [Fact]
    public async Task The_body_rules_apply()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var notAnObject = await host.PostAsync("/v1/pair/verify", "[]");
        using var noBody = await host.SendAsync(host.Request(HttpMethod.Post, "/v1/pair/verify"));

        await Wire.AssertErrorAsync(notAnObject, 400, "INVALID_JSON");
        await Wire.AssertErrorAsync(noBody, 400, "INVALID_JSON");
    }
}
