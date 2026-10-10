import SwiftUI

/// Session detail (screens 13): one stored conversation.
struct SessionDetailScreen: View {
    private let sessionID: UUID

    init(sessionID: UUID) {
        self.sessionID = sessionID
    }

    var body: some View {
        // STUB(WP-11)
        ScreenScaffold {
            SectionTitle("Conversation")
        }
        .navigationTitle("Conversation")
        .navigationBarTitleDisplayMode(.inline)
    }
}
