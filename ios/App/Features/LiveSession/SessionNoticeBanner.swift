import SwiftUI

/// The banner `session-notice`: why the session is paused.
///
/// It stays until Resume succeeds or the session ends, and shows nothing while there is no notice. The
/// banner announces itself once, assertively, when it appears.
struct SessionNoticeBanner: View {
    @Environment(LiveSessionViewModel.self) private var model

    var body: some View {
        if let notice = model.notice {
            Banner(tone: .warning, title: notice.title, message: notice.message)
                .accessibilityIdentifier("session-notice")
        }
    }
}
