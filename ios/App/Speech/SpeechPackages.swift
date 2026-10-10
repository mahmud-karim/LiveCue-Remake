import FluidAudio
import Voz

/// The two on-device speech packages the app will drive.
///
/// Nothing here loads or downloads a model. Naming one type from each package is enough to make the
/// linker resolve both, which is what the build pipeline needs to prove for now.
enum SpeechPackages {
    /// The streaming recogniser from FluidAudio and the batch recogniser from Voz, by qualified name.
    static var linkedTypeNames: [String] {
        [
            String(reflecting: StreamingEouAsrManager.self),
            String(reflecting: Voz.self)
        ]
    }
}
