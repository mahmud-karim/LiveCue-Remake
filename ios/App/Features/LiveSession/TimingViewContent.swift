import Foundation
import LiveCueCore

struct TimingRow: Equatable, Identifiable {
    let id: String
    let key: String
    let value: String
}

/// What the timing view shows for one answer. Pure, so it is unit-tested without a view.
struct TimingViewContent: Equatable {
    /// "<model used> · <effort used>"
    let caption: String
    /// Screens 4.6, in order.
    let rows: [TimingRow]
    /// "N input · M output tokens"
    let usageCaption: String?
    /// "PC timing unavailable. Restart the updated desktop relay."
    let warning: String?
    /// "<speech id> · <N> transcript characters" (+ " · same-text retry")
    let footerCaption: String
    /// S-20 or S-21.
    let footnote: String

    /// The id of the row a Parakeet or Fluid Speech Mini session adds, which the view draws after the
    /// usage caption and the warning.
    static let lastChunkRowID = "lastChunk"

    /// nil without a performance record.
    init?(turn: AssistantTurn) {
        guard let performance = turn.performance else {
            return nil
        }
        let configuration = performance.configuration
        let execution = performance.execution

        // What actually ran, when the service said so; otherwise what was selected.
        let model = execution?.model ?? configuration.model
        let effort = execution?.reasoningEffort ?? configuration.reasoningEffort
        caption = "\(model) · \(effort)"

        var rows: [TimingRow] = [
            TimingRow(id: "total", key: "Tap → answer", value: TimingViewContent.seconds(performance.totalMs)),
            TimingRow(
                id: "transcription",
                key: performance.reusedText ? "Transcription (reused)" : "Transcription after tap",
                value: TimingViewContent.seconds(performance.transcriptionMs)
            ),
            TimingRow(id: "context", key: "Context + model check", value: TimingViewContent.seconds(performance.contextMs))
        ]

        var usageCaption: String? = nil
        var warning: String? = nil
        switch configuration.provider {
        case .codex:
            rows.append(TimingRow(id: "roundTrip", key: "PC round trip", value: TimingViewContent.seconds(performance.roundTripMs)))
            if let execution {
                let transfer = performance.transportEstimateMs ?? 0
                rows.append(TimingRow(id: "codex", key: "PC: Codex CLI call", value: TimingViewContent.seconds(execution.codexMs)))
                rows.append(TimingRow(id: "relayOverhead", key: "PC: relay overhead", value: TimingViewContent.seconds(execution.relayOverheadMs)))
                rows.append(TimingRow(id: "transfer", key: "Transfer / client estimate", value: TimingViewContent.seconds(transfer)))
            } else {
                warning = "PC timing unavailable. Restart the updated desktop relay."
            }
        case .openrouter:
            rows.append(
                TimingRow(id: "roundTrip", key: "OpenRouter round trip", value: TimingViewContent.seconds(performance.roundTripMs))
            )
            let cost = AnswerFormat.reportedCost(of: turn)
            rows.append(
                TimingRow(id: "cost", key: "Reported request cost", value: cost.map { AnswerFormat.credits($0) } ?? "Unavailable")
            )
            if let usage = execution?.usage, usage.promptTokens != nil || usage.completionTokens != nil {
                usageCaption = "\(usage.promptTokens ?? 0) input · \(usage.completionTokens ?? 0) output tokens"
            }
        }

        if let chunk = performance.lastLiveChunkMs {
            rows.append(
                TimingRow(id: TimingViewContent.lastChunkRowID, key: "Last live STT chunk", value: AnswerFormat.milliseconds(chunk))
            )
        }

        self.rows = rows
        self.usageCaption = usageCaption
        self.warning = warning

        var footer = "\(performance.speechModel) · \(performance.transcriptCharacters) transcript characters"
        if performance.reusedText {
            footer += " · same-text retry"
        }
        footerCaption = footer

        switch configuration.provider {
        case .codex:
            footnote = TimingViewContent.codexFootnote
        case .openrouter:
            footnote = TimingViewContent.openRouterFootnote
        }
    }

    /// S-20 of spec 01 section 8.4.
    static let codexFootnote = "PC timings are parts of the round trip, not extra time. Codex includes CLI startup, cloud processing and output handling—not pure inference. Transfer is an estimate including encoding/decoding. Live transcription runs before the tap, so it is not part of tap-to-answer time."

    /// S-21 of spec 01 section 8.4.
    static let openRouterFootnote = "OpenRouter round trip includes network, provider processing and response decoding—not pure inference. Cost is reported by OpenRouter after the request; missing cost is not treated as free. Speech transcription is a separate service."

    /// "0.000 s"
    private static func seconds(_ milliseconds: Double) -> String {
        AnswerFormat.seconds(milliseconds: milliseconds, digits: 3)
    }
}
