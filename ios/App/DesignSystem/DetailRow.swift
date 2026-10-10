import SwiftUI

/// A key on the left and its value on the right, in tabular digits.
struct DetailRow: View {
    private let key: String
    private let value: String
    private let valueTone: StatusTone?

    init(_ key: String, value: String, valueTone: StatusTone? = nil) {
        self.key = key
        self.value = value
        self.valueTone = valueTone
    }

    var body: some View {
        // STUB(WP-05)
        HStack {
            Text(key).lcText(.body)
            Spacer()
            Text(value).lcText(.body, tabular: true)
        }
    }
}
