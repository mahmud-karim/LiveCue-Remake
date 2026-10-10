import Foundation

/// Classification of an HTTP outcome (protocol 13.6), as one pure function.
enum RelayOutcome {
    /// Rows 4 to 12 of protocol 13.6: success bytes, or the failure the outcome stands for.
    ///
    /// The client decides the first three rows before it calls this: cancellation (row 1, which is also
    /// how it reads `TransportFailure.cancelled`), an endpoint that forms no URL (row 2) and the phone
    /// timeout (row 3). Success bytes are those of any 2xx reply; a body that then does not decode as
    /// the route's success shape is `L-UNREADABLE` (row 8), which the client reports.
    ///
    /// A failure made from an HTTP reply carries the reply's status. `L-HTTP` shows it, and so does an
    /// unknown code that comes without a usable relay text.
    static func classify(
        route: RelayRoute,
        result: Result<(HTTPResponseHead, Data), TransportFailure>
    ) -> Result<Data, RelayFailure> {
        switch result {
        case .failure(let failure):
            return .failure(RelayFailure.local(condition(for: failure)))
        case .success(let reply):
            return classify(route: route, status: reply.0.status, body: reply.1)
        }
    }

    /// Rows 4 to 6: there was no HTTP response.
    private static func condition(for failure: TransportFailure) -> RelayLocalCondition {
        switch failure {
        case .tls:
            return .tls
        case .unreachable, .timedOut:
            // A transport time-out is "no answer from the PC", not the phone's own route timeout.
            return .unreachable
        case .other, .cancelled:
            return .network
        }
    }

    /// Rows 7 to 12: the relay, or whatever stands in front of it, answered.
    private static func classify(route: RelayRoute, status: Int, body: Data) -> Result<Data, RelayFailure> {
        if (200...299).contains(status) {
            return .success(body)
        }

        // Row 9. `subscript(key:)` is nil unless the body is a JSON object.
        let value = JSONValue.parse(body)
        if let code = value?["code"]?.stringValue {
            // Protocol 6.8: both answers of route 9 mean "nothing more to do".
            if case .cancel = route, code == RelayWireCode.requestNotFound.rawValue {
                return .success(body)
            }
            let relayText = value?["error"]?.stringValue ?? value?["message"]?.stringValue
            return .failure(RelayFailure(kind: .wire(code), origin: .http, httpStatus: status, relayText: relayText))
        }

        // Row 10.
        if status == 401 {
            return .failure(
                RelayFailure(kind: .wire(RelayWireCode.unauthorized.rawValue), origin: .http, httpStatus: status)
            )
        }
        // Row 11: the edge proxy answering for a relay that is not running.
        if (502...504).contains(status) {
            return .failure(RelayFailure.local(.noRelay, httpStatus: status))
        }
        // Row 12. A 3xx lands here: the relay never redirects and the phone never follows one.
        return .failure(RelayFailure.local(.http, httpStatus: status))
    }
}
