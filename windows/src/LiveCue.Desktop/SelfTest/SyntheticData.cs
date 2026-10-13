using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveCue.Relay;
using Net.Codecrete.QrCodeGenerator;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// Everything the self-test shows (W-design 5.13): a PC on a reserved test domain, a token that is one
/// character repeated, one question and its answer. Nothing here is read from this PC, so the
/// screenshots hold no real address, name, path or pairing.
/// </summary>
internal static class SyntheticData
{
    /// <summary>The name the scripted Tailscale check reports: a reserved test domain.</summary>
    public const string DnsName = "pc.example.test";

    /// <summary>The Private-mode address of that name with the default port.</summary>
    public const string Endpoint = "https://pc.example.test:11443/";

    /// <summary>The Codex the scripted check "finds": the mockup's placeholder, not a path of this PC.</summary>
    public const string CodexPath = @"%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe";

    /// <summary>The version the scripted check reports and the scripted settings record as checked.</summary>
    public const string CodexVersion = "codex-cli 0.0.0-selftest";

    public const string RequestId = "selftest-0001";
    public const string FailedRequestId = "selftest-0002";

    public const string Question = "What is the main advantage of local transcription?";
    public const string PartialTranscript = "and does it still work without a network connection";
    public const string Instruction = "answer briefly";
    public const string Answer = "Your audio stays on the phone.";

    public const string Model = "gpt-5.6-sol";
    public const string ReasoningEffort = "low";
    public const double CodexMs = 850;
    public const double DurationMs = 912;

    /// <summary>The relay's text of ASSISTANT_FAILED, as a failed request would carry it.</summary>
    public const string AssistantFailedMessage = "Assistant request failed or timed out. Check Codex sign-in and retry.";

    // What the relay says about the stored pairing and the PC speech model (protocol 4.12).
    public const string PairingRestored =
        "Saved pairing restored. Existing iPhones reconnect automatically; Show QR keeps their pairing valid.";

    public const string PairingReset = "Pairing reset. Every iPhone must pair again.";
    public const string PortInUse = "The relay port is already in use.";
    public const string SpeechModel = "nemotron";
    public const string SpeechLoading = "Loading Nemotron on the PC GPU…";
    public const string SpeechReady = "Nemotron ready · PC GPU · no speech API cost.";
    public const string SpeechWords = "Local words";

    /// <summary>How many filler entries the activity bound is tried with, and how long each is.</summary>
    public const int FillerEntries = 400;
    public const int FillerLength = 1000;

    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// A token of the right shape (43 characters) that is no token: one character, repeated. It exists
    /// only inside the pairing matrix; the self-test fails if it turns up as text anywhere.
    /// </summary>
    public static string Token { get; } = new('A', 43);

    /// <summary>The pairing payload of protocol 4.2 for the synthetic endpoint and token.</summary>
    public static string PairingPayload => $"{{\"endpoint\":\"{Endpoint}\",\"token\":\"{Token}\"}}";

    /// <summary>The body of the synthetic assist request, compact, as the relay publishes it.</summary>
    public static string RequestPayload { get; } = JsonSerializer.Serialize(
        new Dictionary<string, string>
        {
            ["transcript"] = Question,
            ["partialTranscript"] = PartialTranscript,
            ["instruction"] = Instruction,
        },
        Compact);

    /// <summary>The reply to it, compact, as the relay publishes it.</summary>
    public static string ReplyPayload { get; } = JsonSerializer.Serialize(
        new Dictionary<string, object>
        {
            ["answer"] = Answer,
            ["execution"] = new Dictionary<string, string> { ["model"] = Model, ["reasoningEffort"] = ReasoningEffort },
        },
        Compact);

    /// <summary>
    /// The matrix of <see cref="PairingPayload"/>, made the way the relay makes one (protocol 4.2): one
    /// byte-mode segment, error correction level M, the smallest version that holds it, the level not
    /// raised. Square, row-major, true = dark, no quiet zone. So the window draws a code of the real size.
    /// </summary>
    public static bool[][] PairingMatrix()
    {
        var segments = new List<DataSegment>(1)
        {
            DataSegment.MakeSegment(DataSegmentMode.Binary, new ArraySegment<byte>(Encoding.ASCII.GetBytes(PairingPayload))),
        };
        var code = QrCode.EncodeSegments(segments, QrCode.Ecc.Medium, 1, 40, boostEcl: false);

        var modules = new bool[code.Size][];
        for (var row = 0; row < code.Size; row++)
        {
            modules[row] = new bool[code.Size];
            for (var column = 0; column < code.Size; column++)
            {
                modules[row][column] = code.GetModule(column, row);
            }
        }

        return modules;
    }

    public static RequestStarted RequestStarted(DateTimeOffset at, string requestId) => new(
        at, requestId, AnswerKind.Assist, Model, ReasoningEffort, Question, PartialTranscript, Instruction, RequestPayload);

    public static RequestCompleted RequestCompleted(DateTimeOffset at, string requestId) => new(
        at, requestId, AnswerKind.Assist, DurationMs, CodexMs, Model, ReasoningEffort, ReplyPayload);

    public static RequestFailed RequestFailed(DateTimeOffset at, string requestId) => new(
        at, requestId, AnswerKind.Assist, Relay.Protocol.RelayCodes.AssistantFailed, AssistantFailedMessage);

    /// <summary>One activity line of <see cref="FillerLength"/> characters, different for every index.</summary>
    public static string Filler(int index)
    {
        var head = string.Create(CultureInfo.InvariantCulture, $"Synthetic activity entry {index + 1:000} ");
        return head + new string('.', FillerLength - head.Length);
    }
}
