import SwiftUI

/// Home, the root of the Live tab (screens 3).
struct HomeScreen: View {
    var body: some View {
        // STUB(WP-10)
        ScreenScaffold(scrolls: false) {
            BrandName()
        }
        .toolbar(.hidden, for: .navigationBar)
    }
}
