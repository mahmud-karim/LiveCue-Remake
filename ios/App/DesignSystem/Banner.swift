import SwiftUI

/// A banner (design system 4.11): one accessibility element that reads the title, then the message.
struct Banner: View {
    private let tone: BannerTone
    private let title: String
    private let message: String

    init(tone: BannerTone, title: String, message: String) {
        self.tone = tone
        self.title = title
        self.message = message
    }

    var body: some View {
        // STUB(WP-05)
        VStack(alignment: .leading) {
            Text(title).lcText(.headline)
            Text(message).lcText(.body)
        }
        .accessibilityElement(children: .combine)
    }
}
