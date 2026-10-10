using System.Text;
using System.Text.Json;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Routes;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// Protocol 9.1: the status object as the phone decodes it, strictly. Exactly five members in a fixed
/// order, each of its documented JSON type, whatever the state.
/// </summary>
public sealed class LocalModelStatusJsonTests
{
    public static TheoryData<LocalModelState, string?, bool, int, string, string> Rows => new()
    {
        {
            LocalModelState.Stopped, null, false, 0, FixedTexts.ModelNotSetUp,
            """{"state":"stopped","model":null,"busy":false,"elapsedSeconds":0,"message":"PC speech models are not set up on this PC. Set the speech lab folder in LiveCue Desktop."}"""
        },
        {
            LocalModelState.Stopped, null, true, 0, FixedTexts.ModelStopped,
            """{"state":"stopped","model":null,"busy":true,"elapsedSeconds":0,"message":"PC model is stopped. Start opens Docker Desktop if needed."}"""
        },
        {
            LocalModelState.Starting, "nemotron", false, 659, FixedTexts.ModelStarting,
            """{"state":"starting","model":"nemotron","busy":false,"elapsedSeconds":659,"message":"Starting Docker and loading the PC model. First load can take several minutes."}"""
        },
        {
            LocalModelState.Ready, "nemotron", false, 0, FixedTexts.ModelReady,
            """{"state":"ready","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Model loaded on the PC GPU. Ready for live transcription."}"""
        },
        {
            LocalModelState.Ready, "qwen3", true, 0, FixedTexts.ModelInUse,
            """{"state":"ready","model":"qwen3","busy":true,"elapsedSeconds":0,"message":"Model is in use. Stop the conversation or lab test before unloading."}"""
        },
        {
            LocalModelState.Stopping, "qwen3", false, 44, FixedTexts.ModelStopping,
            """{"state":"stopping","model":"qwen3","busy":false,"elapsedSeconds":44,"message":"Unloading the PC model to free GPU memory…"}"""
        },
        {
            LocalModelState.Error, "nemotron", false, 0, FixedTexts.ModelStartFailed,
            """{"state":"error","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Could not load the PC model. Check Docker Desktop, available GPU memory and the installed model weights, then retry."}"""
        },
        {
            LocalModelState.Error, "qwen3", true, 0, FixedTexts.ModelStopFailed,
            """{"state":"error","model":"qwen3","busy":true,"elapsedSeconds":0,"message":"Could not stop the PC model. Stop any active lab test and retry."}"""
        },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void All_five_members_are_always_written_and_model_is_null_only_when_stopped(
        LocalModelState state, string? model, bool busy, int elapsedSeconds, string message, string expected)
    {
        var bytes = LocalModelStatusRoute.ToJson(new LocalModelStatus(state, model, busy, elapsedSeconds, message));

        // The exact bytes: compact, the members in order, non-ASCII text as UTF-8 and never escaped.
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));

        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Assert.Equal(
            ["state", "model", "busy", "elapsedSeconds", "message"],
            root.EnumerateObject().Select(member => member.Name));
        Assert.Equal(JsonValueKind.String, root.GetProperty("state").ValueKind);
        Assert.Equal(state.ToString().ToLowerInvariant(), root.GetProperty("state").GetString());
        Assert.Equal(
            state == LocalModelState.Stopped ? JsonValueKind.Null : JsonValueKind.String,
            root.GetProperty("model").ValueKind);
        Assert.True(root.GetProperty("busy").ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("elapsedSeconds").ValueKind);

        // An integer with no fraction and no exponent, never a number in quotes.
        Assert.Matches(@"\A(0|[1-9][0-9]*)\z", root.GetProperty("elapsedSeconds").GetRawText());
        Assert.Equal(JsonValueKind.String, root.GetProperty("message").ValueKind);
        Assert.Equal(message, root.GetProperty("message").GetString());
    }

    [Fact]
    public void The_five_state_names_are_the_lower_case_names_of_the_protocol()
    {
        var names = Enum.GetValues<LocalModelState>()
            .Select(state => new LocalModelStatus(state, Model: null, Busy: false, ElapsedSeconds: 0, Message: string.Empty))
            .Select(status => JsonDocument.Parse(LocalModelStatusRoute.ToJson(status)).RootElement.GetProperty("state").GetString());

        Assert.Equal(["stopped", "starting", "ready", "stopping", "error"], names);
    }

    [Fact]
    public void The_nine_status_messages_are_the_texts_of_the_protocol()
    {
        Assert.Equal("PC model is stopped.", FixedTexts.ModelStoppedInitial);
        Assert.Equal(
            "PC speech models are not set up on this PC. Set the speech lab folder in LiveCue Desktop.",
            FixedTexts.ModelNotSetUp);
        Assert.Equal("PC model is stopped. Start opens Docker Desktop if needed.", FixedTexts.ModelStopped);
        Assert.Equal("Model loaded on the PC GPU. Ready for live transcription.", FixedTexts.ModelReady);
        Assert.Equal("Model is in use. Stop the conversation or lab test before unloading.", FixedTexts.ModelInUse);
        Assert.Equal(
            "Starting Docker and loading the PC model. First load can take several minutes.",
            FixedTexts.ModelStarting);
        Assert.Equal("Unloading the PC model to free GPU memory…", FixedTexts.ModelStopping);
        Assert.Equal(
            "Could not load the PC model. Check Docker Desktop, available GPU memory and the installed model weights, then retry.",
            FixedTexts.ModelStartFailed);
        Assert.Equal("Could not stop the PC model. Stop any active lab test and retry.", FixedTexts.ModelStopFailed);
    }
}
