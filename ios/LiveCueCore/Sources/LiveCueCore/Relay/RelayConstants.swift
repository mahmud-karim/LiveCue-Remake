import Foundation

/// The constants of protocol section 2 that the phone uses. The comment names each constant's id.
public enum RelayConstants {
    public static let protocolVersion = 2                    // C-01
    public static let tokenLength = 43                       // C-02: [A-Za-z0-9_-]
    public static let maximumEncodedBodyBytes = 126_976      // C-11
    public static let assistTranscriptBytes = 80_000         // C-12
    public static let summaryTranscriptBytes = 100_000       // C-13
    public static let partialTranscriptBytes = 16_000        // C-14
    public static let instructionCharacters = 4_000          // C-15
    public static let instructionBytes = 8_000               // C-15
    public static let memorySummaryBytes = 24_000            // C-16
    public static let memoryListItems = 16                   // C-17
    public static let memoryListItemBytes = 256              // C-17
    public static let pairingPayloadBytes = 8_192            // C-18
    public static let maximumInboundSpeechMessageBytes = 131_072   // C-21
    public static let speechConnectSeconds: TimeInterval = 180     // C-59
    public static let speechFinalizeSeconds: TimeInterval = 15     // C-60
    public static let healthPollSeconds: TimeInterval = 5          // C-61
    public static let pcModelPollSeconds: TimeInterval = 2         // C-61
    public static let speechSampleRate = 16_000              // C-70
    public static let speechFrameBytes = 2_560               // C-71
    public static let unknownCodeTextMaximumCharacters = 300 // protocol 14
    public static let speechModels: [String] = ["nemotron", "qwen3"]   // C-04
}
