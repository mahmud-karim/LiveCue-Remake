using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Contracts;

/// <summary>
/// Every constant against protocol section 2 and the route deadlines of protocol 6.1. The numbers are
/// written out here a second time on purpose: a change in ProtocolConstants.cs that is not also a change
/// of the protocol fails this test.
/// </summary>
public sealed class ProtocolConstantsTests
{
    [Fact]
    public void Every_value_matches_the_protocol()
    {
        // 2.1 Shapes
        Assert.Equal(2, ProtocolConstants.ProtocolVersion);                                   // C-01
        Assert.Equal(43, ProtocolConstants.TokenLength);                                      // C-02
        Assert.Equal(80, ProtocolConstants.RequestIdMaxLength);                               // C-03
        Assert.Equal(["nemotron", "qwen3"], ProtocolConstants.SpeechModels);                  // C-04
        Assert.Equal(
            ["none", "minimal", "low", "medium", "high", "xhigh", "max"],
            ProtocolConstants.ReasoningEfforts);                                              // C-05
        Assert.Equal(64, ProtocolConstants.ModelIdMaxLength);                                 // C-06
        Assert.Equal(
            ["gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5"],
            ProtocolConstants.AlwaysNoneModels);                                              // C-07

        // 2.2 Sizes
        Assert.Equal(131_072, ProtocolConstants.MaxRequestBodyBytes);                         // C-10
        Assert.Equal(2_048, ProtocolConstants.MaxRequestLineBytes);                           // C-19
        Assert.Equal(16_384, ProtocolConstants.MaxHeaderBytes);                               // C-19
        Assert.Equal(64, ProtocolConstants.MaxHeaderCount);                                   // C-19
        Assert.Equal(16_384, ProtocolConstants.MaxPhoneFrameBytes);                           // C-20
        Assert.Equal(131_072, ProtocolConstants.MaxRelayFrameBytes);                          // C-21
        Assert.Equal(32_000, ProtocolConstants.MaxTranscriptUnits);                           // C-22
        Assert.Equal(131_072, ProtocolConstants.MaxLabMessageBytes);                          // C-23
        Assert.Equal(1_048_576, ProtocolConstants.MaxCodexOutputBytes);                       // C-24
        Assert.Equal(256, ProtocolConstants.MaxSpeechTextBytes);                              // C-25

        // 2.3 Concurrency
        Assert.Equal(64, ProtocolConstants.MaxConnections);                                   // C-30
        Assert.Equal(16, ProtocolConstants.MaxRequestsInFlight);                              // C-31
        Assert.Equal(4, ProtocolConstants.MaxSpeechSockets);                                  // C-32

        // 2.4 Time
        Assert.Equal(TimeSpan.FromSeconds(10), ProtocolConstants.HeadersTimeout);             // C-40
        Assert.Equal(TimeSpan.FromSeconds(10), ProtocolConstants.BodyTimeout);                // C-41
        Assert.Equal(TimeSpan.FromSeconds(30), ProtocolConstants.KeepAliveTimeout);           // C-42
        Assert.Equal(TimeSpan.FromSeconds(60), ProtocolConstants.AnswerDeadline);             // C-43
        Assert.Equal(TimeSpan.FromSeconds(12), ProtocolConstants.CatalogAttemptTimeout);      // C-44
        Assert.Equal(3, ProtocolConstants.CatalogAttempts);                                   // C-44
        Assert.Equal(TimeSpan.FromMilliseconds(50), ProtocolConstants.CatalogRetryGap);       // C-44
        Assert.Equal(TimeSpan.FromMinutes(5), ProtocolConstants.CatalogCacheLife);            // C-44
        Assert.Equal(TimeSpan.FromSeconds(1.5), ProtocolConstants.LabProbeTimeout);           // C-45
        Assert.Equal(TimeSpan.FromSeconds(660), ProtocolConstants.LabStartTimeout);           // C-46
        Assert.Equal(TimeSpan.FromSeconds(45), ProtocolConstants.LabStopTimeout);             // C-46
        Assert.Equal(TimeSpan.FromSeconds(5), ProtocolConstants.LabHandshakeTimeout);         // C-47
        Assert.Equal(TimeSpan.FromSeconds(120), ProtocolConstants.ModelErrorLatch);           // C-48
        Assert.Equal(TimeSpan.FromSeconds(10), ProtocolConstants.SelfCheckTimeout);           // C-49
        Assert.Equal(TimeSpan.FromSeconds(2), ProtocolConstants.LoadingFrameInterval);        // C-50
        Assert.Equal(TimeSpan.FromSeconds(170), ProtocolConstants.ModelLoadLimit);            // C-51
        Assert.Equal(TimeSpan.FromSeconds(20), ProtocolConstants.AudioIdleLimit);             // C-52
        Assert.Equal(TimeSpan.FromSeconds(12), ProtocolConstants.FinalizeLimit);              // C-53
        Assert.Equal(TimeSpan.FromMinutes(30), ProtocolConstants.StreamLimit);                // C-54
        Assert.Equal(TimeSpan.FromMinutes(35), ProtocolConstants.SocketLifetime);             // C-55
        Assert.Equal(TimeSpan.FromSeconds(2), ProtocolConstants.ControlChangeLimit);          // C-56
        Assert.Equal(TimeSpan.FromSeconds(15), ProtocolConstants.KeepAliveFrameInterval);     // C-57
        Assert.Equal(TimeSpan.FromSeconds(2), ProtocolConstants.CloseHandshakeWait);          // C-58

        // 2.5 Audio
        Assert.Equal(16_000, ProtocolConstants.AudioSampleRate);                              // C-70
        Assert.Equal(2_560, ProtocolConstants.AudioFrameBytes);                               // C-71
        Assert.Equal(160_000, ProtocolConstants.AllowanceBaseBytes);                          // C-72
        Assert.Equal(35, ProtocolConstants.AllowanceBytesPerMs);                              // C-72
        Assert.Equal(160_000, ProtocolConstants.LabBacklogBytes);                             // C-73

        // 6.1 Route deadlines
        Assert.Equal(TimeSpan.FromSeconds(5), ProtocolConstants.HealthDeadline);
        Assert.Equal(TimeSpan.FromSeconds(5), ProtocolConstants.PairVerifyDeadline);
        Assert.Equal(TimeSpan.FromSeconds(40), ProtocolConstants.ModelsDeadline);
        Assert.Equal(TimeSpan.FromSeconds(5), ProtocolConstants.LocalModelStatusDeadline);
        Assert.Equal(TimeSpan.FromSeconds(10), ProtocolConstants.LocalModelStartDeadline);
        Assert.Equal(TimeSpan.FromSeconds(10), ProtocolConstants.LocalModelStopDeadline);
        Assert.Equal(TimeSpan.FromSeconds(60), ProtocolConstants.AssistDeadline);
        Assert.Equal(TimeSpan.FromSeconds(60), ProtocolConstants.SessionSummaryDeadline);
        Assert.Equal(TimeSpan.FromSeconds(5), ProtocolConstants.CancelDeadline);

        // 5.1, 5.4, 9.2
        Assert.Equal(240, ProtocolConstants.MinBodyBytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(5), ProtocolConstants.MinBodyRateGrace);
        Assert.Equal(32, ProtocolConstants.MaxJsonDepth);
        Assert.Equal(1, ProtocolConstants.LabRelayProtocol);
    }

    [Fact]
    public void The_numbers_the_windows_design_adds_match_its_table()
    {
        Assert.Equal(4_096, ProtocolConstants.EventChannelCapacity);                          // W-01
        Assert.Equal(TimeSpan.FromSeconds(60), ProtocolConstants.DroppedEventReportInterval); // W-01
        Assert.Equal(TimeSpan.FromSeconds(2.5), ProtocolConstants.StopBudget);                // W-02
        Assert.Equal(256, ProtocolConstants.MaxPhoneQueueFrames);                             // W-03
        Assert.Equal(TimeSpan.FromSeconds(10), ProtocolConstants.PhoneWriteStall);            // W-04
        Assert.Equal(65_536, ProtocolConstants.MaxStateFileBytes);                            // W-05
        Assert.Equal(4_096, ProtocolConstants.MaxProbeBodyBytes);                             // W-06
        Assert.Equal(4, ProtocolConstants.MaxSelfChecks);                                     // W-07
        Assert.Equal(TimeSpan.FromSeconds(5), ProtocolConstants.KilledProcessWait);           // W-08
        Assert.Equal(3, ProtocolConstants.FolderDeleteAttempts);                              // W-09
        Assert.Equal(TimeSpan.FromMilliseconds(100), ProtocolConstants.FolderDeleteGap);      // W-09
        Assert.Equal(TimeSpan.FromSeconds(1), ProtocolConstants.DesktopTick);                 // W-10
        Assert.Equal(TimeSpan.FromSeconds(3.5), ProtocolConstants.WindowCloseWait);           // W-11
        Assert.Equal(TimeSpan.FromSeconds(120), ProtocolConstants.SelfTestWatchdog);          // W-12
        Assert.Equal(TimeSpan.FromSeconds(60), ProtocolConstants.EdgeStatisticsInterval);     // W-13
    }

    [Fact]
    public void The_old_relays_port_is_the_one_port_a_relay_never_binds()
    {
        Assert.Equal(47831, ProtocolConstants.ForbiddenPort);
    }
}
