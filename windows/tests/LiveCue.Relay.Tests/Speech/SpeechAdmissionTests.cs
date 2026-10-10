using System.Net.WebSockets;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol 10.2, steps S-1 to S-6 (P2-104, P2-109, P2-110): the upgrade is always accepted, and a
/// refusal is one typed <c>error</c> frame followed by a close with 1008 and the code as its reason.
/// </summary>
public sealed class SpeechAdmissionTests
{
    [Theory]
    [InlineData("../arbitrary")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Nemotron")]
    [InlineData("nemotron x")]
    [InlineData("nemotron,qwen3")]
    [InlineData("qwen3; powershell -c calc")]
    [InlineData(@"C:\models\nemotron")]
    public async Task An_unknown_or_missing_model_is_a_typed_refusal_and_nothing_is_called(string? model)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await rig.OpenAsync(model);

        var ending = await phone.ReadToEndAsync();

        Assert.Equal(
            ["""{"type":"error","code":"INVALID_SPEECH_MODEL","message":"Unknown speech model."}"""],
            ending.Frames);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, ending.Status);
        Assert.Equal("INVALID_SPEECH_MODEL", ending.Reason);
        Assert.Equal(0, rig.Model.PrepareCalls);
        Assert.Equal(0, rig.Lab.ConnectCalls);

        // Nothing of the header reached the desktop either.
        await rig.Events.SettleAsync();
        Assert.Empty(rig.Events.OfType<SpeechStreamChanged>());
        Assert.Empty(rig.Host.Processes.Requests);
    }

    [Fact]
    public async Task Paused_is_a_typed_refusal()
    {
        await using var rig = await SpeechRig.StartAsync();
        await rig.Relay.SetAcceptingAsync(false);
        using var phone = await rig.OpenAsync();

        var ending = await phone.ReadToEndAsync();

        Assert.Equal(
            ["""{"type":"error","code":"RELAY_PAUSED","message":"PC relay is paused. Resume it in LiveCue Desktop."}"""],
            ending.Frames);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, ending.Status);
        Assert.Equal("RELAY_PAUSED", ending.Reason);
        Assert.Equal(0, rig.Model.PrepareCalls);

        // After a resume the next attempt is admitted.
        await rig.Relay.SetAcceptingAsync(true);
        using var later = await rig.OpenReadyAsync();
    }

    [Fact]
    public async Task A_second_stream_is_speech_busy_and_the_first_continues()
    {
        await using var rig = await SpeechRig.StartAsync();
        using var first = await rig.OpenReadyAsync();
        using var second = await rig.OpenAsync("qwen3");

        var refused = await second.ReadToEndAsync();

        Assert.Equal(
            ["""{"type":"error","code":"SPEECH_BUSY","message":"Another speech stream is active."}"""],
            refused.Frames);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, refused.Status);
        Assert.Equal("SPEECH_BUSY", refused.Reason);
        Assert.Equal(1, rig.Model.PrepareCalls);
        Assert.Equal(1, rig.Lab.ConnectCalls);

        // The first stream is unaffected: it still transcribes and ends cleanly.
        await first.SendAudioAsync(2560);
        await first.ExpectAsync("transcript");
        await first.SendEndStreamAsync();
        var ending = await first.ReadToEndAsync();
        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
        Assert.Equal(["speechComplete", "streamComplete"], ending.Types);
    }

    [Theory]
    [InlineData("1", "nemotron", "PROTOCOL_MISMATCH")]
    [InlineData(null, "nemotron", "PROTOCOL_MISMATCH")]
    [InlineData("2.0", "nemotron", "PROTOCOL_MISMATCH")]
    [InlineData("2,2", "nemotron", "PROTOCOL_MISMATCH")]
    [InlineData("3", "../arbitrary", "PROTOCOL_MISMATCH")]
    [InlineData("2", "whisper", "INVALID_SPEECH_MODEL")]
    public async Task The_protocol_header_is_checked_first_and_then_the_model(string? protocol, string model, string code)
    {
        // Everything after S-2 would refuse as well: the first failing step is the answer.
        await using var rig = await SpeechRig.StartAsync(model =>
        {
            model.IsConfigured = false;
            model.OperationRunning = true;
        });
        await rig.Relay.SetAcceptingAsync(false);
        using var phone = await rig.OpenAsync(model, protocol);

        var ending = await phone.ReadToEndAsync();

        var error = Assert.Single(ending.Errors);
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, ending.Status);
        Assert.Equal(code, ending.Reason);
    }

    [Fact]
    public async Task Steps_three_to_six_refuse_in_their_order()
    {
        await using var rig = await SpeechRig.StartAsync(model =>
        {
            model.IsConfigured = false;
            model.OperationRunning = true;
        });
        // S-3 before S-4 before S-5 before S-6: each step is reached only when the one before it passes.
        await rig.Relay.SetAcceptingAsync(false);
        Assert.Equal("RELAY_PAUSED 1008", await rig.AttemptAsync());

        await rig.Relay.SetAcceptingAsync(true);
        Assert.Equal("LAB_NOT_CONFIGURED 1008", await rig.AttemptAsync());

        rig.Model.IsConfigured = true;
        Assert.Equal("MODEL_OPERATION_IN_PROGRESS 1008", await rig.AttemptAsync());

        rig.Model.OperationRunning = false;
        using var admitted = await rig.OpenReadyAsync();
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        // No refusal took the lease, prepared a model or opened a lab socket.
        Assert.Equal(1, rig.Model.PrepareCalls);
        Assert.Equal(1, rig.Lab.ConnectCalls);
    }
}
