using System.Net;
using System.Net.WebSockets;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-115: what the desktop does reaches an open stream at once. A pause ends it with
/// RELAY_PAUSED, a pairing reset with PAIRING_CHANGED, a stop with RELAY_STOPPING and close code 1001.
/// The clock is never advanced here: "within C-56" is met by not waiting at all.
/// </summary>
public sealed class SpeechControlTests
{
    [Theory]
    [InlineData("preparing")]
    [InlineData("connecting")]
    [InlineData("awaiting-lab-ready")]
    [InlineData("streaming")]
    [InlineData("finalizing")]
    public async Task Pause_ends_the_stream(string state)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);

        await rig.Relay.SetAcceptingAsync(false);

        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "RELAY_PAUSED", WebSocketCloseStatus.InternalServerError);
        Assert.Equal(
            """{"type":"error","code":"RELAY_PAUSED","message":"PC relay is paused. Resume it in LiveCue Desktop."}""",
            ending.Frames[^1]);
        await SpeechWait.UntilAsync(() => rig.Lab.Sockets.ToList().TrueForAll(lab => lab.Disposed), "the lab socket is disposed");

        // While paused the next attempt is refused at admission; after a resume it is admitted.
        Assert.Equal("RELAY_PAUSED 1008", await rig.AttemptAsync());
        await rig.Relay.SetAcceptingAsync(true);
        rig.Model.PrepareGate = null;
        rig.Lab.ConnectGate = null;
        using var next = await rig.OpenAdmittedAsync();
    }

    [Theory]
    [InlineData("preparing")]
    [InlineData("streaming")]
    [InlineData("finalizing")]
    public async Task A_reset_ends_the_stream(string state)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);
        var oldToken = rig.Host.Token;

        await rig.Relay.ResetPairingAsync(CancellationToken.None);

        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "PAIRING_CHANGED", WebSocketCloseStatus.InternalServerError);
        Assert.Equal(
            """{"type":"error","code":"PAIRING_CHANGED","message":"Pairing changed on the PC."}""",
            ending.Frames[^1]);

        // The token the stream was opened with no longer opens anything.
        using var refused = await rig.Host.SendAsync(
            rig.Host.Request(HttpMethod.Get, "/v1/health", TestTokens.Bearer(oldToken)));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_stream_opened_after_a_reset_is_not_ended_by_the_reset_before_it()
    {
        await using var rig = await SpeechRig.StartAsync();
        await rig.Relay.ResetPairingAsync(CancellationToken.None);
        await rig.Host.ReadTokenAsync();
        using var socket = rig.Host.CreateSpeechSocket();
        await socket.ConnectAsync(rig.Host.SpeechUri, CancellationToken.None);
        using var phone = new SpeechPhone(socket);

        await phone.WaitForAsync("ready");
        await phone.SendAudioAsync(2560);

        await phone.ExpectAsync("transcript");
    }

    [Theory]
    [InlineData("preparing")]
    [InlineData("streaming")]
    public async Task Stopping_ends_the_stream_with_1001(string state)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);

        var stop = rig.Relay.StopAsync(CancellationToken.None);
        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "RELAY_STOPPING", WebSocketCloseStatus.EndpointUnavailable);
        await stop.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(
            """{"type":"error","code":"RELAY_STOPPING","message":"The PC relay is stopping."}""",
            ending.Frames[^1]);
        Assert.Equal(RelayStatus.Stopped, rig.Relay.Status);
    }
}
