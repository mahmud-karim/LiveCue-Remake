import Foundation

/// Live captions from a model that takes whole windows (D-29).
///
/// The audio that is not committed yet is transcribed again as it grows. After each such pass this type
/// decides what has become final and how much audio can go, and gives the text for the screen: what is
/// committed, then what this pass heard after it. Times are seconds from the start of the window the
/// pass was given.
public struct RollingTranscript: Equatable, Sendable {
    public struct Word: Equatable, Sendable {
        public var text: String
        public var start: Double
        public var end: Double

        public init(text: String, start: Double, end: Double) {
            self.text = text
            self.start = start
            self.end = end
        }
    }

    /// What a pass led to.
    public enum Rule: Equatable, Sendable {
        /// The speaker paused: every word is committed.
        case pause
        /// Nothing was said: the window is cut down.
        case silence
        /// The window grew long: the words before its widest gap are committed.
        case length
        /// The window is nearly full and has no usable gap: everything is committed.
        case hardCut
    }

    public struct Outcome: Equatable, Sendable {
        /// The committed text, then the words of this pass that are not committed, single-spaced.
        public let text: String
        /// How much audio goes from the start of the window, in seconds. 0 keeps all of it.
        public let dropSeconds: Double
        /// nil when the pass committed nothing and no audio goes.
        public let rule: Rule?

        public init(text: String, dropSeconds: Double, rule: Rule?) {
            self.text = text
            self.dropSeconds = dropSeconds
            self.rule = rule
        }
    }

    /// A pause: the last word ended at least this long before the end of the window.
    public static let pauseSeconds: Double = 1.2
    /// The audio kept after a pause.
    public static let pauseKeepSeconds: Double = 0.3
    /// A window without words is cut down once it is longer than this.
    public static let silenceSeconds: Double = 2
    /// The audio kept of a window without words.
    public static let silenceKeepSeconds: Double = 0.5
    /// From this length on, a window is committed in part.
    public static let lengthSeconds: Double = 11
    /// The gap a long window is cut at lies after this much of it.
    public static let lengthHeadSeconds: Double = 3
    /// The gap a long window is cut at ends this long before the window does.
    public static let lengthTailSeconds: Double = 2
    /// From this length on, a window with no such gap is committed whole.
    public static let hardCutSeconds: Double = 14
    /// The audio kept after a window was committed whole.
    public static let hardCutKeepSeconds: Double = 0.2

    public private(set) var committed = ""
    /// What the last pass heard and did not commit.
    public private(set) var pending = ""

    public init() {}

    /// What is on screen: the committed text, then what the last pass left open.
    public var text: String {
        RollingTranscript.joined(committed, pending)
    }

    /// One pass over a window of `duration` seconds. `words` is empty when the pass gave no times; its
    /// `text` then stands for all of it and can only be committed whole.
    public mutating func apply(duration: Double, text: String, words: [Word]) -> Outcome {
        let heard = RollingTranscript.singleSpaced(words.isEmpty ? text : RollingTranscript.text(of: words[...]))

        if let last = words.last, duration - last.end >= RollingTranscript.pauseSeconds {
            commit(heard)
            return Outcome(text: committed, dropSeconds: max(0, duration - RollingTranscript.pauseKeepSeconds), rule: .pause)
        }

        if heard.isEmpty {
            pending = ""
            guard duration > RollingTranscript.silenceSeconds else {
                return Outcome(text: committed, dropSeconds: 0, rule: nil)
            }
            return Outcome(text: committed, dropSeconds: duration - RollingTranscript.silenceKeepSeconds, rule: .silence)
        }

        if duration >= RollingTranscript.lengthSeconds, let cut = RollingTranscript.widestGap(in: words, duration: duration) {
            commit(RollingTranscript.singleSpaced(RollingTranscript.text(of: words[...cut.index])))
            pending = RollingTranscript.singleSpaced(RollingTranscript.text(of: words[(cut.index + 1)...]))
            return Outcome(text: self.text, dropSeconds: min(max(0, cut.middle), duration), rule: .length)
        }

        if duration >= RollingTranscript.hardCutSeconds {
            commit(heard)
            return Outcome(text: committed, dropSeconds: max(0, duration - RollingTranscript.hardCutKeepSeconds), rule: .hardCut)
        }

        pending = heard
        return Outcome(text: self.text, dropSeconds: 0, rule: nil)
    }

    /// Commits what the last pass left open and returns the whole text.
    public mutating func finish() -> String {
        commit(pending)
        return committed
    }

    public mutating func reset() {
        committed = ""
        pending = ""
    }

    // MARK: - Pieces

    /// Adds `text` to the committed text. Nothing stays open.
    private mutating func commit(_ text: String) {
        committed = RollingTranscript.joined(committed, text)
        pending = ""
    }

    /// The place a long window is cut at: between two words of which the earlier ends at or after
    /// `lengthHeadSeconds` and the later starts `lengthTailSeconds` or more before the end. Of those
    /// places the one with the widest gap, and of equal gaps the later one. nil when there is none.
    private static func widestGap(in words: [Word], duration: Double) -> (index: Int, middle: Double)? {
        guard words.count > 1 else { return nil }
        var best: (index: Int, gap: Double)?
        for index in 0..<(words.count - 1) {
            let earlier = words[index]
            let later = words[index + 1]
            guard earlier.end >= lengthHeadSeconds, later.start <= duration - lengthTailSeconds else { continue }
            let gap = later.start - earlier.end
            if let found = best, gap < found.gap {
                continue
            }
            best = (index, gap)
        }
        guard let best else { return nil }
        return (best.index, (words[best.index].end + words[best.index + 1].start) / 2)
    }

    private static func text(of words: ArraySlice<Word>) -> String {
        words.map(\.text).joined(separator: " ")
    }

    /// The words of `text` with one space between them and none around them.
    private static func singleSpaced(_ text: String) -> String {
        text.split(whereSeparator: \.isWhitespace).joined(separator: " ")
    }

    private static func joined(_ first: String, _ second: String) -> String {
        if first.isEmpty {
            return second
        }
        if second.isEmpty {
            return first
        }
        return first + " " + second
    }
}
