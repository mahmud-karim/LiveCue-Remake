import SwiftUI

/// A row that pushes `route`. Title and value form the link's accessibility label and value. The subtitle
/// is laid over the link without hit testing and stays its own accessibility element, so an identifier
/// set inside it is reachable by UI tests. `identifier` goes on the link.
struct LCNavigationRow<Subtitle: View>: View {
    private let route: Route
    private let title: String
    private let value: String?
    private let valueTone: StatusTone?
    private let systemImage: String?
    private let identifier: String?
    private let subtitle: Subtitle

    init(
        route: Route,
        title: String,
        value: String? = nil,
        valueTone: StatusTone? = nil,
        systemImage: String? = nil,
        identifier: String? = nil,
        @ViewBuilder subtitle: () -> Subtitle
    ) {
        self.route = route
        self.title = title
        self.value = value
        self.valueTone = valueTone
        self.systemImage = systemImage
        self.identifier = identifier
        self.subtitle = subtitle()
    }

    var body: some View {
        // STUB(WP-05)
        VStack(alignment: .leading) {
            NavigationLink(value: route) {
                HStack {
                    Text(title).lcText(.body)
                    Spacer()
                    if let value {
                        Text(value).lcText(.body)
                    }
                }
            }
            .accessibilityIdentifier(identifier ?? "")
            subtitle
        }
    }
}

extension LCNavigationRow where Subtitle == EmptyView {
    init(
        route: Route,
        title: String,
        value: String? = nil,
        valueTone: StatusTone? = nil,
        systemImage: String? = nil,
        identifier: String? = nil
    ) {
        self.init(
            route: route,
            title: title,
            value: value,
            valueTone: valueTone,
            systemImage: systemImage,
            identifier: identifier,
            subtitle: { EmptyView() }
        )
    }
}
