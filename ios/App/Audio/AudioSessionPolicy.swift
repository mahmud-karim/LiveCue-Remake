import AVFoundation

/// Category, mode, options and activation rules of the audio session (architecture 4.6).
///
/// One policy serves every provider: play-and-record in measurement mode (no voice processing), routed to
/// the speaker by default, with Bluetooth hands-free microphones allowed. No sample rate and no buffer
/// duration is asked for; capture takes the hardware as it is. With the session active and the engine
/// running, the `audio` background mode keeps the app alive behind the lock screen; nothing else is done
/// for the background.
///
/// Nothing here belongs to an actor. The blocks the system calls on its own threads are built here for
/// that reason: a closure written inside a main-actor method would be bound to the main actor.
enum AudioSessionPolicy {
    static var category: AVAudioSession.Category {
        .playAndRecord
    }

    static var mode: AVAudioSession.Mode {
        .measurement
    }

    /// Default to the speaker, and allow Bluetooth hands-free devices as the microphone.
    static var options: AVAudioSession.CategoryOptions {
        [.defaultToSpeaker, .allowBluetooth]
    }

    /// The buffer size asked of the tap. The system may deliver another size.
    static let tapBufferFrames: AVAudioFrameCount = 2_048

    /// Level events are published at most this often.
    static let levelsPerSecond: Double = 24

    /// Configures the session and activates it. Throws what the session throws.
    static func activate(_ session: AVAudioSession) throws {
        try session.setCategory(category, mode: mode, options: options)
        // The haptics of the live screen stay felt while the microphone is open. A refusal is no reason
        // to refuse the capture.
        try? session.setAllowHapticsAndSystemSoundsDuringRecording(true)
        try session.setActive(true)
    }

    /// Deactivates the session and lets other apps take their audio back. Failures are ignored: there is
    /// nothing left to stop.
    static func deactivate(_ session: AVAudioSession) {
        try? session.setActive(false, options: .notifyOthersOnDeactivation)
    }

    /// A format the tap can be installed with: the input reports a sample rate and at least one channel.
    static func isUsable(_ format: AVAudioFormat) -> Bool {
        AudioInputDiagnosis.isUsable(sampleRate: format.sampleRate, channelCount: Int(format.channelCount))
    }

    /// What the session says right now, for a start that gives up. Read before the session is released.
    /// Routes are given by port type, never by the name of a device.
    static func report(
        _ session: AVAudioSession,
        stage: AudioStartReport.Stage,
        format: AVAudioFormat?,
        converterBuilt: Bool
    ) -> AudioStartReport {
        AudioStartReport(
            stage: stage,
            isInputAvailable: session.isInputAvailable,
            routeInputs: session.currentRoute.inputs.map { $0.portType.rawValue },
            sampleRate: format?.sampleRate ?? 0,
            channelCount: Int(format?.channelCount ?? 0),
            converterBuilt: converterBuilt
        )
    }

    /// `.interrupted` when an interruption began. nil when it ended or the notification says nothing.
    static func event(forInterruption notification: Notification) -> AudioCaptureEvent? {
        guard let raw = notification.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
              AVAudioSession.InterruptionType(rawValue: raw) == .began else {
            return nil
        }
        return .interrupted
    }

    /// `.routeLost` when the device in use went away. nil for every other reason: a new device, a
    /// category change or an override does not stop the capture.
    static func event(forRouteChange notification: Notification) -> AudioCaptureEvent? {
        guard let raw = notification.userInfo?[AVAudioSessionRouteChangeReasonKey] as? UInt,
              AVAudioSession.RouteChangeReason(rawValue: raw) == .oldDeviceUnavailable else {
            return nil
        }
        return .routeLost
    }

    /// The observer of `AVAudioSession.interruptionNotification` for one capture.
    static func interruptionObserver(reportingTo converter: AudioTapConverter) -> @Sendable (Notification) -> Void {
        return { notification in
            if let event = AudioSessionPolicy.event(forInterruption: notification) {
                converter.send(event)
            }
        }
    }

    /// The observer of `AVAudioSession.routeChangeNotification` for one capture.
    static func routeChangeObserver(reportingTo converter: AudioTapConverter) -> @Sendable (Notification) -> Void {
        return { notification in
            if let event = AudioSessionPolicy.event(forRouteChange: notification) {
                converter.send(event)
            }
        }
    }
}
