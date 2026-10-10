using System.Net;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Contracts;

/// <summary>The solution is wired: a real relay on a real listener answers health.</summary>
public sealed class SmokeTests
{
    [Fact]
    public async Task Health_answers()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var withToken = await host.GetAsync("/v1/health");
        using var withoutToken = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", authorization: null));

        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Equal(
            """{"ok":true,"protocol":2,"relayVersion":"1.0.0"}""",
            await withToken.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
        Assert.Equal(
            """{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}""",
            await withoutToken.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_route_of_the_foundation_answers_as_its_first_version_says()
    {
        await using var host = await RelayTestHost.StartAsync(options => options.LabConfigured = false);

        using var verify = await host.PostAsync("/v1/pair/verify", "{}");
        using var models = await host.GetAsync("/v1/models");
        using var status = await host.GetAsync("/v1/local-model");
        using var start = await host.PostAsync("/v1/local-model/start", Bodies.LocalModel("nemotron"));
        using var assist = await host.PostAsync("/v1/assist", Bodies.Assist());
        using var summary = await host.PostAsync("/v1/session-summary", Bodies.Summary());
        using var cancel = await host.SendAsync(host.Request(HttpMethod.Delete, "/v1/requests/" + Bodies.RequestId));

        Assert.Equal("""{"ok":true}""", await verify.Content.ReadAsStringAsync());
        Assert.Equal(
            """{"models":[{"id":"gpt-test","name":"Test","reasoningEfforts":["low"]}]}""",
            await models.Content.ReadAsStringAsync());
        Assert.Equal(
            """{"state":"stopped","model":null,"busy":false,"elapsedSeconds":0,"message":"PC speech models are not set up on this PC. Set the speech lab folder in LiveCue Desktop."}""",
            await status.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, start.StatusCode);
        Assert.Contains("LAB_NOT_CONFIGURED", await start.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, assist.StatusCode);
        Assert.Contains("\"answer\":\"Answer.\"", await assist.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Contains("REQUEST_NOT_FOUND", await cancel.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_speech_socket_is_accepted_and_refused_with_a_typed_frame()
    {
        // Without a lab folder the real bridge refuses at step S-4, as the foundation's stand-in always did.
        await using var host = await RelayTestHost.StartAsync(options =>
        {
            options.UseSystemClock = true;
            options.LabConfigured = false;
        });
        using var socket = await host.OpenSpeechAsync();

        var buffer = new byte[1024];
        var frame = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, frame.Count);
        var close = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(
            """{"type":"error","code":"LAB_NOT_CONFIGURED","message":"PC speech models are not set up on this PC."}""",
            text);
        Assert.Equal(System.Net.WebSockets.WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
        Assert.Equal("LAB_NOT_CONFIGURED", close.CloseStatusDescription);
    }
}
