namespace LiveCue.Relay.Protocol;

/// <summary>
/// Every number of relay protocol 2 (docs/architecture/02-relay-protocol.md, section 2), the route
/// deadlines of its section 6.1, and the numbers the Windows design adds (W-nn, section 4.1 there).
/// This is the only C# file in which a protocol number is written; everything else names a member.
/// </summary>
public static class ProtocolConstants
{
    // ---- 2.1 Shapes ----------------------------------------------------------------------------

    /// <summary>C-01. Sent by the phone in X-LiveCue-Protocol; reported by health.</summary>
    public const int ProtocolVersion = 2;

    /// <summary>C-02. A token is 32 random bytes as 43 unpadded base64url characters.</summary>
    public const int TokenLength = 43;

    /// <summary>C-03. A request id is 1 to this many characters of [A-Za-z0-9_-].</summary>
    public const int RequestIdMaxLength = 80;

    /// <summary>C-04. The PC speech models, exactly.</summary>
    public static IReadOnlyList<string> SpeechModels { get; } = ["nemotron", "qwen3"];

    /// <summary>C-05. The reasoning efforts the relay offers, in this order.</summary>
    public static IReadOnlyList<string> ReasoningEfforts { get; } =
        ["none", "minimal", "low", "medium", "high", "xhigh", "max"];

    /// <summary>C-07. Answer models that always offer "none".</summary>
    public static IReadOnlyList<string> AlwaysNoneModels { get; } =
        ["gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5"];

    // ---- 2.2 Sizes -----------------------------------------------------------------------------

    /// <summary>C-10. Hard limit of an HTTP request body, in bytes.</summary>
    public const int MaxRequestBodyBytes = 131_072;

    /// <summary>C-19. Longest request line, in bytes.</summary>
    public const int MaxRequestLineBytes = 2_048;

    /// <summary>C-19. All request headers together, in bytes.</summary>
    public const int MaxHeaderBytes = 16_384;

    /// <summary>C-19. Most request headers.</summary>
    public const int MaxHeaderCount = 64;

    /// <summary>C-20. Largest speech frame from the phone, in bytes.</summary>
    public const int MaxPhoneFrameBytes = 16_384;

    /// <summary>C-21. Largest speech frame the relay sends, in bytes.</summary>
    public const int MaxRelayFrameBytes = 131_072;

    /// <summary>C-22. Longest transcript in a speech frame, in UTF-16 code units.</summary>
    public const int MaxTranscriptUnits = 32_000;

    /// <summary>C-23. Largest message from the lab, in bytes.</summary>
    public const int MaxLabMessageBytes = 131_072;

    /// <summary>C-24. Largest Codex output file, and most catalogue process output, in bytes.</summary>
    public const int MaxCodexOutputBytes = 1_048_576;

    /// <summary>C-25. Largest speech text frame from the phone, in bytes.</summary>
    public const int MaxSpeechTextBytes = 256;

    // ---- 2.3 Concurrency -----------------------------------------------------------------------

    /// <summary>C-30. Most TCP connections to the relay.</summary>
    public const int MaxConnections = 64;

    /// <summary>C-31. Most authenticated HTTP requests in flight.</summary>
    public const int MaxRequestsInFlight = 16;

    /// <summary>C-32. Most open speech sockets, admitted or being refused.</summary>
    public const int MaxSpeechSockets = 4;

    // ---- 2.4 Time ------------------------------------------------------------------------------

    /// <summary>C-40. Request headers must arrive within this time.</summary>
    public static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(10);

    /// <summary>C-41. A request body must arrive within this time after the headers.</summary>
    public static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(10);

    /// <summary>C-42. An idle keep-alive connection is closed after this time.</summary>
    public static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(30);

    /// <summary>C-43. Deadline of a whole assist or summary request.</summary>
    public static readonly TimeSpan AnswerDeadline = TimeSpan.FromSeconds(60);

    /// <summary>C-44. Limit of one catalogue read attempt.</summary>
    public static readonly TimeSpan CatalogAttemptTimeout = TimeSpan.FromSeconds(12);

    /// <summary>C-44. Catalogue read attempts.</summary>
    public const int CatalogAttempts = 3;

    /// <summary>C-44. Gap between catalogue read attempts.</summary>
    public static readonly TimeSpan CatalogRetryGap = TimeSpan.FromMilliseconds(50);

    /// <summary>C-44. How long a read catalogue stays fresh.</summary>
    public static readonly TimeSpan CatalogCacheLife = TimeSpan.FromMinutes(5);

    /// <summary>C-45. Lab health probe.</summary>
    public static readonly TimeSpan LabProbeTimeout = TimeSpan.FromSeconds(1.5);

    /// <summary>C-46. Lab start script.</summary>
    public static readonly TimeSpan LabStartTimeout = TimeSpan.FromSeconds(660);

    /// <summary>C-46. Lab stop script.</summary>
    public static readonly TimeSpan LabStopTimeout = TimeSpan.FromSeconds(45);

    /// <summary>C-47. Lab WebSocket handshake.</summary>
    public static readonly TimeSpan LabHandshakeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>C-48. How long a failed model operation is reported as an error.</summary>
    public static readonly TimeSpan ModelErrorLatch = TimeSpan.FromSeconds(120);

    /// <summary>C-49. Pairing self-check.</summary>
    public static readonly TimeSpan SelfCheckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>C-50. Interval of the "loading" speech frame.</summary>
    public static readonly TimeSpan LoadingFrameInterval = TimeSpan.FromSeconds(2);

    /// <summary>C-51. Model load limit of a speech stream, counted from the upgrade.</summary>
    public static readonly TimeSpan ModelLoadLimit = TimeSpan.FromSeconds(170);

    /// <summary>C-52. No audio received while streaming.</summary>
    public static readonly TimeSpan AudioIdleLimit = TimeSpan.FromSeconds(20);

    /// <summary>C-53. Finalization after endStream.</summary>
    public static readonly TimeSpan FinalizeLimit = TimeSpan.FromSeconds(12);

    /// <summary>C-54. Stream length, counted from "ready".</summary>
    public static readonly TimeSpan StreamLimit = TimeSpan.FromMinutes(30);

    /// <summary>C-55. Speech socket lifetime, counted from the upgrade.</summary>
    public static readonly TimeSpan SocketLifetime = TimeSpan.FromMinutes(35);

    /// <summary>C-56. A pause or pairing change takes effect on an open stream within this time.</summary>
    public static readonly TimeSpan ControlChangeLimit = TimeSpan.FromSeconds(2);

    /// <summary>C-57. Interval of the WebSocket keep-alive frame the relay sends.</summary>
    public static readonly TimeSpan KeepAliveFrameInterval = TimeSpan.FromSeconds(15);

    /// <summary>C-58. Close handshake wait before a socket is aborted.</summary>
    public static readonly TimeSpan CloseHandshakeWait = TimeSpan.FromSeconds(2);

    // ---- 2.5 Audio -----------------------------------------------------------------------------

    /// <summary>C-70. Samples per second of the raw PCM audio (signed 16-bit little-endian, mono).</summary>
    public const int AudioSampleRate = 16_000;

    /// <summary>C-71. Bytes of one 80 ms audio frame.</summary>
    public const int AudioFrameBytes = 2_560;

    /// <summary>C-72. Byte allowance at "ready".</summary>
    public const int AllowanceBaseBytes = 160_000;

    /// <summary>C-72. Byte allowance added per millisecond since "ready".</summary>
    public const int AllowanceBytesPerMs = 35;

    /// <summary>C-73. Bytes accepted from the phone and not yet written to the lab.</summary>
    public const int LabBacklogBytes = 160_000;

    // ---- Route deadlines (protocol 6.1), counted from the arrival of the request headers --------

    /// <summary>Route 1.</summary>
    public static readonly TimeSpan HealthDeadline = TimeSpan.FromSeconds(5);

    /// <summary>Route 2.</summary>
    public static readonly TimeSpan PairVerifyDeadline = TimeSpan.FromSeconds(5);

    /// <summary>Route 3.</summary>
    public static readonly TimeSpan ModelsDeadline = TimeSpan.FromSeconds(40);

    /// <summary>Route 4.</summary>
    public static readonly TimeSpan LocalModelStatusDeadline = TimeSpan.FromSeconds(5);

    /// <summary>Route 5.</summary>
    public static readonly TimeSpan LocalModelStartDeadline = TimeSpan.FromSeconds(10);

    /// <summary>Route 6.</summary>
    public static readonly TimeSpan LocalModelStopDeadline = TimeSpan.FromSeconds(10);

    /// <summary>Route 7 (C-43).</summary>
    public static readonly TimeSpan AssistDeadline = AnswerDeadline;

    /// <summary>Route 8 (C-43).</summary>
    public static readonly TimeSpan SessionSummaryDeadline = AnswerDeadline;

    /// <summary>Route 9.</summary>
    public static readonly TimeSpan CancelDeadline = TimeSpan.FromSeconds(5);

    // ---- Other values of the protocol ----------------------------------------------------------

    /// <summary>Protocol 5.1. A request body slower than this, after the grace period, is aborted.</summary>
    public const double MinBodyBytesPerSecond = 240;

    /// <summary>Protocol 5.1. Grace period before the minimum body rate applies.</summary>
    public static readonly TimeSpan MinBodyRateGrace = TimeSpan.FromSeconds(5);

    /// <summary>Protocol 5.4 rule 4. Deepest nesting of a request body.</summary>
    public const int MaxJsonDepth = 32;

    /// <summary>Protocol 9.2. The lab protocol version the relay can use.</summary>
    public const int LabRelayProtocol = 1;

    /// <summary>The old relay's port. A relay never binds it (W-design 4.2 step 3).</summary>
    public const int ForbiddenPort = 47831;

    // ---- Numbers the Windows design adds (W-design 4.1) ----------------------------------------

    /// <summary>W-01. Capacity of the event channel; the oldest event is dropped when it is full.</summary>
    public const int EventChannelCapacity = 4_096;

    /// <summary>W-01. Dropped events are reported once per this interval.</summary>
    public static readonly TimeSpan DroppedEventReportInterval = TimeSpan.FromSeconds(60);

    /// <summary>W-02. Budget of StopAsync before open connections are aborted.</summary>
    public static readonly TimeSpan StopBudget = TimeSpan.FromSeconds(2.5);

    /// <summary>W-03. Frames queued toward the phone; one more aborts the socket.</summary>
    public const int MaxPhoneQueueFrames = 256;

    /// <summary>W-04. A frame not written to the phone within this time aborts the socket.</summary>
    public static readonly TimeSpan PhoneWriteStall = TimeSpan.FromSeconds(10);

    /// <summary>W-05. Largest pairing.json or settings.json that is read, in bytes.</summary>
    public const int MaxStateFileBytes = 65_536;

    /// <summary>W-06. Largest self-check or lab-probe response body that is read, in bytes.</summary>
    public const int MaxProbeBodyBytes = 4_096;

    /// <summary>W-07. Self-checks that may be in progress at once.</summary>
    public const int MaxSelfChecks = 4;

    /// <summary>W-08. Wait for a killed child process to exit before it is abandoned.</summary>
    public static readonly TimeSpan KilledProcessWait = TimeSpan.FromSeconds(5);

    /// <summary>W-09. Tries at deleting a Codex working folder that is still locked.</summary>
    public const int FolderDeleteAttempts = 3;

    /// <summary>W-09. Gap between those tries.</summary>
    public static readonly TimeSpan FolderDeleteGap = TimeSpan.FromMilliseconds(100);

    /// <summary>W-10. Interval of the desktop's presence and countdown tick.</summary>
    public static readonly TimeSpan DesktopTick = TimeSpan.FromSeconds(1);

    /// <summary>W-11. What closing the window waits for the relay: W-02 plus one second.</summary>
    public static readonly TimeSpan WindowCloseWait = StopBudget + TimeSpan.FromSeconds(1);

    /// <summary>W-12. Watchdog of the desktop self-test.</summary>
    public static readonly TimeSpan SelfTestWatchdog = TimeSpan.FromSeconds(120);
}
