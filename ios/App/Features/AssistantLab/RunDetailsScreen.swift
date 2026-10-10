import SwiftUI

/// The details of one compared answer, with its timing shown open.
struct RunDetailsScreen: View {
    private let turnID: UUID

    init(turnID: UUID) {
        self.turnID = turnID
    }

    var body: some View {
        // STUB(WP-10)
        ScreenScaffold {
            SectionTitle("Run details")
        }
        .navigationTitle("Run details")
        .navigationBarTitleDisplayMode(.inline)
    }
}
