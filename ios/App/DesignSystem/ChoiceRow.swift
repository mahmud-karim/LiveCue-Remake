import SwiftUI

/// One choice of a list: a single button with the selected trait and a trailing check.
struct ChoiceRow: View {
    private let title: String
    private let subtitle: String?
    private let isSelected: Bool
    private let action: @MainActor () -> Void

    init(title: String, subtitle: String? = nil, isSelected: Bool, action: @escaping @MainActor () -> Void) {
        self.title = title
        self.subtitle = subtitle
        self.isSelected = isSelected
        self.action = action
    }

    var body: some View {
        // STUB(WP-05)
        Button(action: action) {
            VStack(alignment: .leading) {
                Text(title).lcText(.body)
                if let subtitle {
                    CaptionText(subtitle)
                }
            }
        }
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}
