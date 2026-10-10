using System.Net;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Catalog;

/// <summary>
/// Route 3, <c>GET /v1/models</c> (protocol 6.4; statement P2-036), through a real loopback listener.
/// </summary>
public sealed class ModelsRouteTests
{
    [Fact]
    public async Task A_failing_catalogue_is_503_with_the_code()
    {
        // The real clock: the two gaps between the three attempts are 50 ms each.
        await using var host = await RelayTestHost.StartAsync(options => options.UseSystemClock = true);
        host.Catalog.Fails = true;

        using var response = await host.GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            """{"error":"The PC could not read Codex's model list. Keep Codex signed in, then retry. Your selected model has not changed.","code":"ASSISTANT_CATALOG_UNAVAILABLE"}""",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(ProtocolConstants.CatalogAttempts, host.Catalog.Calls);

        // The desktop is told, with the relay's fixed text.
        var notice = await host.Events.WaitForAsync<AssistantNotice>();
        Assert.Equal(RelayCodes.AssistantCatalogUnavailable, notice.Code);
        Assert.Equal(RelayErrors.AssistantCatalogUnavailable.Text, notice.Message);
        Assert.Equal(1, host.Events.Count<AssistantNotice>());
    }

    [Fact]
    public async Task The_catalogue_is_written_as_id_name_and_efforts()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Catalog.Loader = () =>
        [
            new RawCatalogEntry("gpt-5.6-sol", "GPT-5.6 Sol", Hidden: false, ["low", "medium", "high"]),
            new RawCatalogEntry("gpt-nameless", null, Hidden: false, ["low", "ultra"]),
            new RawCatalogEntry("gpt-hidden", "Hidden", Hidden: true, ["low"]),
            new RawCatalogEntry("gpt-é", "Wrong shape", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-named", "Näme \"quoted\"", Hidden: false, ["none"]),
        ];

        using var response = await host.GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"models":[{"id":"gpt-5.6-sol","name":"GPT-5.6 Sol","reasoningEfforts":["none","low","medium","high"]},{"id":"gpt-nameless","name":"gpt-nameless","reasoningEfforts":["low"]},{"id":"gpt-named","name":"Näme \"quoted\"","reasoningEfforts":["none"]}]}""",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Events.Count<AssistantNotice>());
    }

    [Fact]
    public async Task An_empty_catalogue_is_an_empty_list_and_the_route_is_served_while_paused()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Catalog.Loader = () => [];
        await host.Relay.SetAcceptingAsync(false);

        using var response = await host.GetAsync("/v1/models");
        using var again = await host.GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"models":[]}""", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        // The second request used the source's cached copy.
        Assert.Equal(1, host.Catalog.Calls);
    }

    [Fact]
    public async Task Once_read_the_catalogue_outlives_a_failing_source()
    {
        await using var host = await RelayTestHost.StartAsync();
        using var first = await host.GetAsync("/v1/models");
        host.Catalog.Fails = true;
        host.Clock.Advance(ProtocolConstants.CatalogCacheLife);

        var pending = host.GetAsync("/v1/models");
        while (!pending.IsCompleted)
        {
            host.Clock.Advance(ProtocolConstants.CatalogRetryGap);
            await Task.Delay(5);
        }

        using var second = await pending;

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1 + ProtocolConstants.CatalogAttempts, host.Catalog.Calls);
    }
}
