import SwiftUI

/// The Start control of Home (design system 4.17): a 64 high accent capsule that floats above the tab bar.
/// The caller sets its accessibility identifier.
struct SessionStartControl: View {
    private let title: String
    private let systemImage: String
    private let isBusy: Bool
    private let action: @MainActor () -> Void

    init(title: String, systemImage: String, isBusy: Bool = false, action: @escaping @MainActor () -> Void) {
        self.title = title
        self.systemImage = systemImage
        self.isBusy = isBusy
        self.action = action
    }

    var body: some View {
        // STUB(WP-05)
        LCGlassButton(prominent: true, action: action) {
            Label(title, systemImage: systemImage)
        }
        .disabled(isBusy)
    }
}
