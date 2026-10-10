using System.Net.WebSockets;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-117, everything the lab can do wrong: a wrong first message, an <c>error</c> event, a
/// message that is not a JSON object, <c>streamComplete</c> too early, a socket that closes, fails or
/// cannot be opened. Each has its own code, and nothing the lab wrote reaches the phone.
/// </summary>
public sealed class SpeechLabFailureTests
{
    private const string LabText = "Model is busy.";

    [Theory]
    [InlineData("""{"type":"ready","model":"qwen3","sampleRate":16000}""")]
    [InlineData("""{"type":"error","message":"Model is busy."}""")]
    [InlineData("""{"type":"ready","model":"nemotron","sampleRate":8000}""")]
    [InlineData("""{"type":"ready","model":"nemotron"}""")]
    [InlineData("""{"type":"ready","sampleRate":16000}""")]
    [InlineData("""{"type":"speechStart","turnId":1,"audioProcessedMs":0}""")]
    [InlineData("""{"type":"somethingNew","message":"Model is busy."}""")]
    [InlineData("""{"message":"Model is busy."}""")]
    [InlineData("""["ready"]""")]
    [InlineData("Model is busy.")]
    [InlineData("<binary or oversized>")]
    public async Task Another_model_or_a_busy_error_first_is_model_not_ready(string first)
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.FirstMessage = string.Empty;
        using var phone = await SpeechStates.ReachAsync(rig, "awaiting-lab-ready");

        Send(rig.Lab.Current, first);

        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "MODEL_NOT_READY", WebSocketCloseStatus.InternalServerError);
        Assert.Equal(
            """{"type":"error","code":"MODEL_NOT_READY","message":"The selected PC model is not ready or is busy."}""",
            ending.Frames[^1]);
        Assert.DoesNotContain(ending.Frames, frame => frame.Contains(LabText, StringComparison.Ordinal));
        Assert.DoesNotContain(ending.Frames, frame => frame.Contains("\"ready\"", StringComparison.Ordinal));
        Assert.True(rig.Lab.Current.Disposed);
    }

    [Theory]
    [InlineData("streaming")]
    [InlineData("finalizing")]
    public async Task A_lab_error_event(string state)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);

        rig.Lab.Current.Enqueue("""{"type":"error","message":"Local inference failed.","detail":"private"}""");

        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "LAB_INFERENCE_FAILED", WebSocketCloseStatus.InternalServerError);
        Assert.Equal(
            ["""{"type":"error","code":"LAB_INFERENCE_FAILED","message":"PC speech inference failed."}"""],
            ending.Frames);
    }

    [Theory]
    [InlineData("streaming", """["transcript"]""", "LAB_PROTOCOL_ERROR")]
    [InlineData("streaming", "\"transcript\"", "LAB_PROTOCOL_ERROR")]
    [InlineData("streaming", "not json", "LAB_PROTOCOL_ERROR")]
    [InlineData("streaming", """{"type":"transcript"} {"type":"transcript"}""", "LAB_PROTOCOL_ERROR")]
    [InlineData("streaming", "", "LAB_PROTOCOL_ERROR")]
    [InlineData("streaming", "<binary or oversized>", "LAB_PROTOCOL_ERROR")]
    [InlineData("streaming", """{"type":"streamComplete","audioProcessedMs":80}""", "LAB_PROTOCOL_ERROR")]
    [InlineData("finalizing", "42", "LAB_PROTOCOL_ERROR")]
    [InlineData("finalizing", "<binary or oversized>", "LAB_PROTOCOL_ERROR")]
    public async Task A_non_object_message_or_an_early_stream_complete(string state, string message, string code)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);

        Send(rig.Lab.Current, message);

        var ending = await SpeechFrameRuleTests.AssertEndsAsync(phone, code, WebSocketCloseStatus.InternalServerError);
        Assert.Equal(
            ["""{"type":"error","code":"LAB_PROTOCOL_ERROR","message":"Unexpected local speech response."}"""],
            ending.Frames);
    }

    [Theory]
    [InlineData("""{"type":"audioProgress","audioProcessedMs":80}""")]
    [InlineData("""{"type":"somethingNew","transcript":"hidden"}""")]
    [InlineData("""{"type":"ready","model":"nemotron","sampleRate":16000}""")]
    [InlineData("""{"transcript":"hidden"}""")]
    [InlineData("{}")]
    public async Task A_lab_event_of_another_type_is_dropped_and_the_stream_goes_on(string message)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await rig.OpenReadyAsync();

        rig.Lab.Current.Enqueue(message);
        await phone.SendAudioAsync(2560);

        // The next frame the phone gets is the transcript of its audio: the other event left no trace.
        Assert.Equal("""{"type":"transcript","transcript":"Local words"}""", await phone.ReceiveTextAsync());
        await phone.SendEndStreamAsync();
        var ending = await phone.ReadToEndAsync();
        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
        Assert.DoesNotContain(ending.Frames, frame => frame.Contains("hidden", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("awaiting-lab-ready")]
    [InlineData("streaming")]
    [InlineData("finalizing")]
    public async Task The_lab_closing_early(string state)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);

        rig.Lab.Current.Close();

        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "LAB_DISCONNECTED", WebSocketCloseStatus.InternalServerError);
        Assert.Equal(
            """{"type":"error","code":"LAB_DISCONNECTED","message":"Local model disconnected."}""",
            ending.Frames[^1]);
    }

    [Fact]
    public async Task An_unreachable_lab()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.ConnectFails = true;
        using var phone = await rig.OpenAsync();

        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "LAB_CONNECTION_FAILED", WebSocketCloseStatus.InternalServerError);

        Assert.Equal(
            [
                """{"type":"loading","provider":"nemotron","elapsedSeconds":0,"message":"Loading Nemotron on PC · 0 s. Microphone starts when ready."}""",
                """{"type":"error","code":"LAB_CONNECTION_FAILED","message":"Local model connection failed."}""",
            ],
            ending.Frames);
        Assert.Equal(1, rig.Lab.ConnectCalls);
        Assert.Empty(rig.Lab.Sockets);

        // There is never a fallback to the other model, and the lease is free again.
        Assert.Equal(["nemotron"], rig.Model.PreparedModels);
        rig.Lab.ConnectFails = false;
        using var next = await rig.OpenAdmittedAsync();
    }

    [Theory]
    [InlineData("awaiting-lab-ready", false)]
    [InlineData("streaming", false)]
    [InlineData("streaming", true)]
    [InlineData("finalizing", false)]
    public async Task A_lab_socket_that_fails_is_a_failed_connection(string state, bool onSend)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);
        var lab = rig.Lab.Current;

        lab.Broken = true;
        if (onSend)
        {
            // The next audio frame cannot be written to the lab.
            await phone.SendAudioAsync(2560);
        }
        else
        {
            // The receive that is waiting wakes up and fails.
            lab.Enqueue("{}");
        }

        await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "LAB_CONNECTION_FAILED", WebSocketCloseStatus.InternalServerError);
    }

    private static void Send(FakeLabSocket lab, string message)
    {
        if (message == "<binary or oversized>")
        {
            lab.EnqueueInvalid();
        }
        else
        {
            lab.Enqueue(message);
        }
    }
}
