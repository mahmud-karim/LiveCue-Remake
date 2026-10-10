using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-118 and the close-code table of protocol 10.6: every close other than 1000 is preceded
/// by exactly one <c>error</c> frame, and the close reason is that frame's <c>code</c>. One case per code,
/// and two for the codes that can come both from admission and from a running stream.
/// </summary>
public sealed class SpeechCloseTests
{
    [Theory]
    [InlineData("PROTOCOL_MISMATCH", "admission", 1008)]
    [InlineData("INVALID_SPEECH_MODEL", "admission", 1008)]
    [InlineData("RELAY_PAUSED", "admission", 1008)]
    [InlineData("LAB_NOT_CONFIGURED", "admission", 1008)]
    [InlineData("MODEL_OPERATION_IN_PROGRESS", "admission", 1008)]
    [InlineData("SPEECH_BUSY", "admission", 1008)]
    [InlineData("RELAY_PAUSED", "stream", 1011)]
    [InlineData("MODEL_OPERATION_IN_PROGRESS", "stream", 1011)]
    [InlineData("MODEL_BUSY", "stream", 1011)]
    [InlineData("MODEL_LOAD_FAILED", "stream", 1011)]
    [InlineData("MODEL_LOAD_TIMEOUT", "stream", 1011)]
    [InlineData("LAB_CONNECTION_FAILED", "stream", 1011)]
    [InlineData("MODEL_NOT_READY", "stream", 1011)]
    [InlineData("LAB_INFERENCE_FAILED", "stream", 1011)]
    [InlineData("LAB_PROTOCOL_ERROR", "stream", 1011)]
    [InlineData("LAB_DISCONNECTED", "stream", 1011)]
    [InlineData("AUDIO_OUT_OF_SEQUENCE", "stream", 1011)]
    [InlineData("AUDIO_LIMIT", "stream", 1011)]
    [InlineData("FRAME_TOO_LARGE", "stream", 1009)]
    [InlineData("UNSUPPORTED_COMMAND", "stream", 1011)]
    [InlineData("AUDIO_IDLE_TIMEOUT", "stream", 1011)]
    [InlineData("FINALIZE_TIMEOUT", "stream", 1011)]
    [InlineData("SESSION_LIMIT", "stream", 1011)]
    [InlineData("PAIRING_CHANGED", "stream", 1011)]
    [InlineData("RELAY_STOPPING", "stream", 1001)]
    public async Task Every_abnormal_close_has_exactly_one_error_frame_and_its_code_as_the_reason(
        string code, string from, int closeCode)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var other = from == "admission" && code == "SPEECH_BUSY" ? await rig.OpenReadyAsync() : null;
        using var phone = await CauseAsync(rig, code, from);

        var ending = await phone.ReadToEndAsync();

        // Exactly one error frame, the last frame of the stream, with the fixed relay text of its code.
        var error = Assert.Single(ending.Errors);
        Assert.Equal("error", ending.Types[^1]);
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(RelayErrors.ForCode(code)!.Value.Text, error.GetProperty("message").GetString());
        Assert.Equal(3, error.EnumerateObject().Count());

        // The close code of protocol 10.6, and the code again as the reason.
        Assert.Equal(closeCode, (int?)ending.Status);
        Assert.Equal(code, ending.Reason);

        // A refused socket gets nothing else. An admitted stream got "loading" first (and "ready", where
        // the scene needed it): those are the only frames besides the error.
        if (from == "admission")
        {
            Assert.Equal(["error"], ending.Types);
        }
        else
        {
            Assert.All(ending.Types.Take(ending.Types.Count - 1), type => Assert.Equal("loading", type));
        }
    }

    /// <summary>Opens a stream and does to it what produces that code. Nothing is read past the scene.</summary>
    private static async Task<SpeechPhone> CauseAsync(SpeechRig rig, string code, string from)
    {
        if (from == "admission")
        {
            switch (code)
            {
                case "PROTOCOL_MISMATCH":
                    return await rig.OpenAsync(protocol: "1");
                case "INVALID_SPEECH_MODEL":
                    return await rig.OpenAsync(model: "../arbitrary");
                case "RELAY_PAUSED":
                    await rig.Relay.SetAcceptingAsync(false);
                    return await rig.OpenAsync();
                case "LAB_NOT_CONFIGURED":
                    rig.Model.IsConfigured = false;
                    return await rig.OpenAsync();
                case "MODEL_OPERATION_IN_PROGRESS":
                    rig.Model.OperationRunning = true;
                    return await rig.OpenAsync();
                default:
                    return await rig.OpenAsync();
            }
        }

        SpeechPhone phone;
        switch (code)
        {
            case "RELAY_PAUSED":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                await rig.Relay.SetAcceptingAsync(false);
                return phone;

            case "MODEL_OPERATION_IN_PROGRESS":
                rig.Model.PrepareResult = PrepareOutcome.OperationInProgress;
                return await rig.OpenAsync();

            case "MODEL_BUSY":
                rig.Model.PrepareResult = PrepareOutcome.Busy;
                return await rig.OpenAsync();

            case "MODEL_LOAD_FAILED":
                rig.Model.PrepareResult = PrepareOutcome.LoadFailed;
                return await rig.OpenAsync();

            case "MODEL_LOAD_TIMEOUT":
                rig.Model.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                phone = await rig.OpenAsync();
                await SpeechWait.UntilAsync(() => rig.Model.PrepareCalls == 1, "the prepare began");
                rig.Clock.Advance(ProtocolConstants.ModelLoadLimit);
                return phone;

            case "LAB_CONNECTION_FAILED":
                rig.Lab.ConnectFails = true;
                return await rig.OpenAsync();

            case "MODEL_NOT_READY":
                rig.Lab.Model = "qwen3";
                return await rig.OpenAsync("nemotron");

            case "LAB_INFERENCE_FAILED":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                rig.Lab.Current.Enqueue("""{"type":"error","message":"Local inference failed."}""");
                return phone;

            case "LAB_PROTOCOL_ERROR":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                rig.Lab.Current.Enqueue("[]");
                return phone;

            case "LAB_DISCONNECTED":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                rig.Lab.Current.Close();
                return phone;

            case "AUDIO_OUT_OF_SEQUENCE":
                rig.Model.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                phone = await rig.OpenAsync();
                await phone.SendAudioAsync(2560);
                return phone;

            case "AUDIO_LIMIT":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                await phone.SendAudioAsync(2561);
                return phone;

            case "FRAME_TOO_LARGE":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                await phone.SendAudioAsync(ProtocolConstants.MaxPhoneFrameBytes + 1);
                return phone;

            case "UNSUPPORTED_COMMAND":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                await phone.SendTextAsync("""{"type":"stop"}""");
                return phone;

            case "AUDIO_IDLE_TIMEOUT":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                rig.Clock.Advance(ProtocolConstants.AudioIdleLimit);
                return phone;

            case "FINALIZE_TIMEOUT":
                rig.Lab.AutoRespond = false;
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                await phone.SendEndStreamAsync();
                await SpeechWait.UntilAsync(() => rig.Lab.Current.EndStreams == 1, "the lab got endStream");
                rig.Clock.Advance(ProtocolConstants.FinalizeLimit);
                return phone;

            case "SESSION_LIMIT":
                rig.Lab.AutoRespond = false;
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                var lab = rig.Lab.Current;
                var step = TimeSpan.FromSeconds(15);
                var steps = (int)(ProtocolConstants.StreamLimit / step) - 1;
                for (var sent = 1; sent <= steps; sent++)
                {
                    // Audio keeps arriving, so the idle limit never fires before the stream limit does.
                    rig.Clock.Advance(step);
                    await phone.SendAudioAsync(2);
                    await SpeechWait.UntilAsync(() => lab.AudioFrames.Count == sent, "the audio reached the lab");
                }

                rig.Clock.Advance(step);
                return phone;

            case "PAIRING_CHANGED":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                await rig.Relay.ResetPairingAsync(CancellationToken.None);
                return phone;

            case "RELAY_STOPPING":
                phone = await rig.OpenAsync();
                await phone.WaitForAsync("ready");
                _ = rig.Relay.StopAsync(CancellationToken.None);
                return phone;

            default:
                throw new ArgumentOutOfRangeException(nameof(code));
        }
    }
}
