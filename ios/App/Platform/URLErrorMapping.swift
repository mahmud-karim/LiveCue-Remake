import Foundation
import LiveCueCore

/// The one mapping from system errors to `TransportFailure`.
enum URLErrorMapping {
    /// cancelled and CancellationError: .cancelled. timedOut: .timedOut.
    /// secureConnectionFailed and every serverCertificate and clientCertificate code: .tls.
    /// notConnectedToInternet, networkConnectionLost, cannotConnectToHost, cannotFindHost, dnsLookupFailed: .unreachable.
    /// Everything else: .other.
    ///
    /// A `TransportFailure` is returned as it is, so an error that was mapped once keeps its meaning.
    static func transportFailure(for error: any Error) -> TransportFailure {
        if let failure = error as? TransportFailure {
            return failure
        }
        if error is CancellationError {
            return .cancelled
        }

        // `URLError` and the `NSError` the system hands to delegates are the same thing here.
        let systemError = error as NSError
        guard systemError.domain == NSURLErrorDomain else {
            return .other
        }

        switch systemError.code {
        case NSURLErrorCancelled:
            return .cancelled
        case NSURLErrorTimedOut:
            return .timedOut
        case NSURLErrorSecureConnectionFailed,
             NSURLErrorServerCertificateHasBadDate,
             NSURLErrorServerCertificateUntrusted,
             NSURLErrorServerCertificateHasUnknownRoot,
             NSURLErrorServerCertificateNotYetValid,
             NSURLErrorClientCertificateRejected,
             NSURLErrorClientCertificateRequired:
            return .tls
        case NSURLErrorNotConnectedToInternet,
             NSURLErrorNetworkConnectionLost,
             NSURLErrorCannotConnectToHost,
             NSURLErrorCannotFindHost,
             NSURLErrorDNSLookupFailed:
            return .unreachable
        default:
            return .other
        }
    }
}
