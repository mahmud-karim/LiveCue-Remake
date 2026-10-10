using System.Globalization;

namespace LiveCue.Relay.Protocol;

/// <summary>
/// The fixed English texts the relay produces besides the error table: the status messages of protocol
/// 9.4, the "loading" frame message of protocol 10.3, and the event texts of W-design 4.2 and 4.12.
/// </summary>
internal static class FixedTexts
{
    /// <summary>MSG-1. The controller's stored status before its first probe.</summary>
    public const string ModelStoppedInitial = "PC model is stopped.";

    /// <summary>MSG-2. No lab folder is configured.</summary>
    public const string ModelNotSetUp =
        "PC speech models are not set up on this PC. Set the speech lab folder in LiveCue Desktop.";

    /// <summary>MSG-3.</summary>
    public const string ModelStopped = "PC model is stopped. Start opens Docker Desktop if needed.";

    /// <summary>MSG-4.</summary>
    public const string ModelReady = "Model loaded on the PC GPU. Ready for live transcription.";

    /// <summary>MSG-5.</summary>
    public const string ModelInUse = "Model is in use. Stop the conversation or lab test before unloading.";

    /// <summary>MSG-6.</summary>
    public const string ModelStarting =
        "Starting Docker and loading the PC model. First load can take several minutes.";

    /// <summary>MSG-7.</summary>
    public const string ModelStopping = "Unloading the PC model to free GPU memory…";

    /// <summary>MSG-8.</summary>
    public const string ModelStartFailed =
        "Could not load the PC model. Check Docker Desktop, available GPU memory and the installed model weights, then retry.";

    /// <summary>MSG-9.</summary>
    public const string ModelStopFailed = "Could not stop the PC model. Stop any active lab test and retry.";

    /// <summary>Notice(PairingRestored).</summary>
    public const string PairingRestored =
        "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid.";

    /// <summary>Notice(PairingReset).</summary>
    public const string PairingReset = "Pairing reset. Every iPhone must pair again.";

    /// <summary>The fixed fault messages of W-design 4.2, by code.</summary>
    public static string Fault(RelayFaultCode code) => code switch
    {
        RelayFaultCode.PortInUse => "The relay port is already in use.",
        RelayFaultCode.PairingStoreUnreadable => "The saved pairing could not be read.",
        RelayFaultCode.InvalidEndpoint => "The advertised endpoint is not valid.",
        RelayFaultCode.StartFailed => "Could not start the relay.",
        _ => "The relay stopped after an internal error.",
    };

    /// <summary>"Nemotron" or "Qwen3" for a model of C-04; the model text itself for anything else.</summary>
    public static string ModelLabel(string model) => model switch
    {
        "nemotron" => "Nemotron",
        "qwen3" => "Qwen3",
        _ => model,
    };

    /// <summary>The message of a "loading" frame (protocol 10.3).</summary>
    public static string LoadingFrame(string model, int elapsedSeconds) => string.Create(
        CultureInfo.InvariantCulture,
        $"Loading {ModelLabel(model)} on PC · {elapsedSeconds} s. Microphone starts when ready.");

    /// <summary>SpeechStreamChanged(Loading).</summary>
    public static string SpeechLoading(string model) => $"Loading {ModelLabel(model)} on the PC GPU…";

    /// <summary>SpeechStreamChanged(Ready).</summary>
    public static string SpeechReady(string model) => $"{ModelLabel(model)} ready · PC GPU · no speech API cost.";

    /// <summary>SpeechStreamChanged(Stopped).</summary>
    public static string SpeechStopped(string model) => $"{ModelLabel(model)} transcription stopped.";
}
