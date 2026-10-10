import Foundation
import LiveCueCore

/// The one mapping from system errors to `TransportFailure`.
enum URLErrorMapping {
    /// cancelled and CancellationError: .cancelled. timedOut: .timedOut.
    /// secureConnectionFailed and every serverCertificate and clientCertificate code: .tls.
    /// notConnectedToInternet, networkConnectionLost, cannotConnectToHost, cannotFindHost, dnsLookupFailed: .unreachable.
    /// Everything else: .other.
    static func transportFailure(for error: any Error) -> TransportFailure {
        // STUB(WP-06)
        .other
    }
}
