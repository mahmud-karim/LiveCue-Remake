import SwiftUI

/// The live session screen (screens 4). It replaces the tabs while a session exists.
struct LiveSessionScreen: View {
    var body: some View {
        // STUB(WP-10)
        ScreenScaffold(scrolls: false) {
            SectionTitle("Live session")
        }
        .toolbar(.hidden, for: .navigationBar)
    }
}
