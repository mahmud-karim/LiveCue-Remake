import SwiftUI

/// History, the root of the History tab (screens 13): every stored conversation, newest first.
///
/// Swiping a row offers "Delete", which asks first. The confirmation is this screen's own alert.
struct HistoryScreen: View {
    @Environment(HistoryViewModel.self) private var model

    private static let deleteMessage = "Its transcript and answers are removed from this iPhone. This cannot be undone."

    var body: some View {
        content
            .navigationTitle("History")
            .alert("Delete this conversation?", isPresented: deleteShown, presenting: model.pendingDeleteID) { id in
                Button("Delete", role: .destructive) {
                    delete(id)
                }
                Button("Cancel", role: .cancel) {
                    model.cancelDelete()
                }
            } message: { _ in
                Text(verbatim: HistoryScreen.deleteMessage)
            }
    }

    @ViewBuilder
    private var content: some View {
        if model.isEmpty {
            ScreenScaffold(scrolls: false) {
                EmptyStateView(
                    systemImage: "clock",
                    title: "No conversations yet",
                    message: "Completed sessions will appear here."
                )
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        } else {
            HistoryList()
        }
    }

    /// True while a row waits for its confirmation. Closing the alert drops the request.
    private var deleteShown: Binding<Bool> {
        Binding(
            get: { model.pendingDeleteID != nil },
            set: { isShown in
                if !isShown {
                    model.cancelDelete()
                }
            }
        )
    }

    /// The alert closes as its button is pressed, and closing it drops the request. The id the alert was
    /// shown for is therefore handed back before the deletion is confirmed.
    private func delete(_ id: UUID) {
        Task {
            model.requestDelete(id)
            await model.confirmDelete()
        }
    }
}

/// The conversations as one panel of rows.
///
/// A `List`, because the rows need swipe actions (design system 4.8). The system's row backgrounds and
/// separators are hidden; each row draws its slice of the panel behind itself, so the rows read as one
/// panel on the ground.
private struct HistoryList: View {
    @Environment(HistoryViewModel.self) private var model
    @Environment(AppModel.self) private var app
    @Environment(\.theme) private var theme

    var body: some View {
        let rows = model.rows

        ZStack {
            GroundView()
            List {
                ForEach(rows) { (row: HistoryRowContent) in
                    HistoryListItem(
                        row: row,
                        isFirst: row.id == rows.first?.id,
                        isLast: row.id == rows.last?.id,
                        open: { open(row.id) },
                        delete: { model.requestDelete(row.id) }
                    )
                }
            }
            .listStyle(.plain)
            .environment(\.defaultMinListRowHeight, 0)
            .contentMargins(.top, theme.space.s1, for: .scrollContent)
            .contentMargins(.bottom, theme.space.s6, for: .scrollContent)
            .animation(theme.motion.base, value: rows)
        }
        // What `ScreenScaffold` gives every other screen: no system background, ink text, the tab bar.
        .scrollContentBackground(.hidden)
        .foregroundStyle(theme.ink.primary)
        .lcTabBarChrome()
    }

    private func open(_ id: UUID) {
        app.historyPath.append(Route.sessionDetail(sessionID: id))
    }
}

/// One row of the list with its slice of the panel, its hairline and its swipe action.
private struct HistoryListItem: View {
    let row: HistoryRowContent
    let isFirst: Bool
    let isLast: Bool
    let open: @MainActor () -> Void
    let delete: @MainActor () -> Void

    @Environment(\.theme) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if !isFirst {
                LCHairline()
            }
            HistoryRow(content: row, action: open)
        }
        .padding(.horizontal, theme.space.panelInset)
        .listRowInsets(
            EdgeInsets(top: 0, leading: theme.space.screenInset, bottom: 0, trailing: theme.space.screenInset)
        )
        .listRowSeparator(.hidden)
        .listRowBackground(
            HistoryPanelSlice(isFirst: isFirst, isLast: isLast)
                .padding(.horizontal, theme.space.screenInset)
        )
        .swipeActions(edge: .trailing, allowsFullSwipe: false) {
            // No destructive role: the system would take the row away before the confirmation.
            Button("Delete", action: delete)
                .tint(theme.status.danger)
        }
    }
}

/// The part of the panel behind one row: the panel's fill, with the panel's corners on the first and the
/// last row, and the panel's edge along the outside only.
private struct HistoryPanelSlice: View {
    let isFirst: Bool
    let isLast: Bool

    @Environment(\.theme) private var theme

    var body: some View {
        ZStack {
            shape.fill(theme.surface.panel)
            shape
                .strokeBorder(isFirst ? theme.stroke.edgeTop : theme.stroke.edgeBottom, lineWidth: theme.component.stroke)
                .mask {
                    // The edge between two rows is not an edge of the panel.
                    Rectangle()
                        .padding(.top, isFirst ? 0 : theme.component.stroke)
                        .padding(.bottom, isLast ? 0 : theme.component.stroke)
                }
        }
        .accessibilityHidden(true)
    }

    private var shape: UnevenRoundedRectangle {
        let top = isFirst ? theme.radius.panel : 0
        let bottom = isLast ? theme.radius.panel : 0
        return UnevenRoundedRectangle(
            topLeadingRadius: top,
            bottomLeadingRadius: bottom,
            bottomTrailingRadius: bottom,
            topTrailingRadius: top,
            style: .continuous
        )
    }
}
