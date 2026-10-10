import Foundation

/// Classification of an HTTP outcome (protocol 13.6), as one pure function.
enum RelayOutcome {
    /// Rows 3 to 12 of protocol 13.6: success bytes, or the failure the outcome stands for.
    static func classify(
        route: RelayRoute,
        result: Result<(HTTPResponseHead, Data), TransportFailure>
    ) -> Result<Data, RelayFailure> {
        // STUB(WP-03)
        .failure(RelayFailure.local(.network))
    }
}
