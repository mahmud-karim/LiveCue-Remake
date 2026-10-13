import Foundation

/// Loads either on-device engine. Each engine marks its own model folder excluded from backup.
struct LiveOnDeviceEngineLoader: OnDeviceEngineLoading {
    init() {}

    func loadVoz(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any BatchRecognizing {
        do {
            return try await VozEngine.load(progress: progress)
        } catch {
            throw EngineLoadFailure.readable(error)
        }
    }

    func loadParakeet(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing {
        do {
            return try await ParakeetEngine.load(progress: progress)
        } catch {
            throw EngineLoadFailure.readable(error)
        }
    }
}

/// A speech package's error with the package's own words as its text.
///
/// A failed preparation shows the error's `localizedDescription` (E-12). Some package errors describe
/// themselves only through `description`; shown as they are, the alert would read "The operation
/// couldn't be completed". Those are wrapped here so that the alert carries the package's text.
struct EngineLoadFailure: LocalizedError, Equatable {
    let errorDescription: String?

    /// `error` itself when it already has a text of its own; otherwise a failure that carries its
    /// description.
    static func readable(_ error: any Error) -> any Error {
        if error is CancellationError {
            return error
        }
        if let localized = error as? any LocalizedError, localized.errorDescription != nil {
            return error
        }
        // An error of the system brings its own text.
        if type(of: error) is NSError.Type {
            return error
        }
        if let described = error as? any CustomStringConvertible {
            return EngineLoadFailure(errorDescription: described.description)
        }
        return error
    }
}
