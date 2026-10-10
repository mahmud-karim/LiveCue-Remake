import SwiftUI

/// A header that shows or hides its content (design system 4.24). `identifier` and the two labels go on
/// the header button.
struct DisclosureRow<Content: View>: View {
    private let title: String
    private let isExpanded: Binding<Bool>
    private let identifier: String?
    private let collapsedLabel: String?
    private let expandedLabel: String?
    private let content: Content

    init(
        _ title: String,
        isExpanded: Binding<Bool>,
        identifier: String? = nil,
        collapsedLabel: String? = nil,
        expandedLabel: String? = nil,
        @ViewBuilder content: () -> Content
    ) {
        self.title = title
        self.isExpanded = isExpanded
        self.identifier = identifier
        self.collapsedLabel = collapsedLabel
        self.expandedLabel = expandedLabel
        self.content = content()
    }

    var body: some View {
        // STUB(WP-05)
        VStack(alignment: .leading) {
            Button(title) {
                isExpanded.wrappedValue.toggle()
            }
            .accessibilityLabel(currentLabel)
            .accessibilityIdentifier(identifier ?? "")
            if isExpanded.wrappedValue {
                content
            }
        }
    }

    private var currentLabel: String {
        let label = isExpanded.wrappedValue ? expandedLabel : collapsedLabel
        return label ?? title
    }
}
