import Foundation
import Observation

/// The two flags other owners lock on while a session runs.
/// Written only by `SessionController`. Read by the owners that must lock while a session runs.
@MainActor @Observable final class SessionActivity {
    /// A session exists (the phase is not idle or preflight).
    var isActive: Bool = false
    var isAssisting: Bool = false

    init() {}
}
