using System.Net;
using System.Text.Json;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>Protocol P2-020, route 1: <c>GET /v1/health</c>.</summary>
public sealed class HealthTests
{
    [Fact]
    public async Task Exactly_three_members_also_while_paused()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var running = await host.GetAsync("/v1/health");
        await host.Relay.SetAcceptingAsync(false);
        using var paused = await host.GetAsync("/v1/health");

        foreach (var response in (HttpResponseMessage[])[running, paused])
        {
            var text = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("""{"ok":true,"protocol":2,"relayVersion":"1.0.0"}""", text);
            Assert.Equal(["ok", "protocol", "relayVersion"], root.EnumerateObject().Select(member => member.Name));
            Assert.Equal(JsonValueKind.True, root.GetProperty("ok").ValueKind);
            Assert.Equal(2, root.GetProperty("protocol").GetInt32());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("relayVersion").GetString()));
        }
    }

    [Fact]
    public async Task The_document_does_no_other_work()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var response = await host.GetAsync("/v1/health");
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, host.Catalog.Calls);
        Assert.Equal(0, host.Lab.ProbeCalls);
        Assert.Empty(host.Backend.Runs);
        Assert.Empty(host.Processes.Requests);
        Assert.All(host.Events.Snapshot(), relayEvent => Assert.True(relayEvent is RelayStarted or PhoneSeen));
    }
}
