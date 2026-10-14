import Foundation
import LiveCueCore
import XCTest

/// The rules of the rolling window (D-29): what a pass commits, how much audio goes, and the text on
/// screen after it.
final class RollingTranscriptTests: XCTestCase {
    private typealias Word = RollingTranscript.Word
    private typealias Outcome = RollingTranscript.Outcome

    private func word(_ text: String, _ start: Double, _ end: Double) -> Word {
        Word(text: text, start: start, end: end)
    }

    /// "what is the main", ending at 3.5 s.
    private var question: [Word] {
        [word("what", 1.0, 1.4), word("is", 1.5, 1.8), word("the", 2.0, 2.5), word("main", 3.0, 3.5)]
    }

    /// Eight words up to 10.5 s. Its widest gap that can be cut at is the second between "d" and "e".
    private var longWindow: [Word] {
        [
            word("a", 0.5, 1.0), word("b", 1.2, 2.0), word("c", 2.5, 3.5), word("d", 4.0, 5.0),
            word("e", 6.0, 7.0), word("f", 7.2, 8.0), word("g", 8.5, 9.0), word("h", 9.6, 10.5)
        ]
    }

    private func assertOutcome(
        _ outcome: Outcome,
        text: String,
        drop: Double,
        rule: RollingTranscript.Rule?,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        XCTAssertEqual(outcome.text, text, file: file, line: line)
        XCTAssertEqual(outcome.dropSeconds, drop, accuracy: 0.000_001, file: file, line: line)
        XCTAssertEqual(outcome.rule, rule, file: file, line: line)
    }

    // MARK: - Constants

    func testTheConstantsAreThoseOfTheDecision() {
        XCTAssertEqual(RollingTranscript.pauseSeconds, 1.2)
        XCTAssertEqual(RollingTranscript.pauseKeepSeconds, 0.3)
        XCTAssertEqual(RollingTranscript.silenceSeconds, 2)
        XCTAssertEqual(RollingTranscript.silenceKeepSeconds, 0.5)
        XCTAssertEqual(RollingTranscript.lengthSeconds, 11)
        XCTAssertEqual(RollingTranscript.lengthHeadSeconds, 3)
        XCTAssertEqual(RollingTranscript.lengthTailSeconds, 2)
        XCTAssertEqual(RollingTranscript.hardCutSeconds, 14)
        XCTAssertEqual(RollingTranscript.hardCutKeepSeconds, 0.2)
    }

    func testANewTranscriptIsEmpty() {
        var transcript = RollingTranscript()
        XCTAssertEqual(transcript.committed, "")
        XCTAssertEqual(transcript.pending, "")
        XCTAssertEqual(transcript.text, "")
        XCTAssertEqual(transcript.finish(), "")
    }

    // MARK: - A pass that commits nothing

    func testAPassWithoutARuleShowsItsWordsAndKeepsAllAudio() {
        var transcript = RollingTranscript()

        // The last word ends half a second before the end: the speaker is still talking.
        let first = transcript.apply(duration: 3, text: "what is the", words: Array(question.prefix(3)))
        assertOutcome(first, text: "what is the", drop: 0, rule: nil)
        XCTAssertEqual(transcript.committed, "")
        XCTAssertEqual(transcript.pending, "what is the")

        // The next pass hears the same audio again and more. What was open is replaced, not added to.
        let heardAgain = [word("what", 1.0, 1.4), word("was", 1.5, 1.8), word("the", 2.0, 2.5), word("main", 3.0, 3.5)]
        let second = transcript.apply(duration: 4, text: "what was the main", words: heardAgain)
        assertOutcome(second, text: "what was the main", drop: 0, rule: nil)
        XCTAssertEqual(transcript.committed, "")
        XCTAssertEqual(transcript.text, "what was the main")
    }

    func testTheTextOnScreenIsSingleSpaced() {
        var transcript = RollingTranscript()
        let spaced = [word(" what ", 1.0, 1.4), word("is\n", 1.5, 1.8)]
        XCTAssertEqual(transcript.apply(duration: 2, text: "ignored when there are words", words: spaced).text, "what is")

        var untimed = RollingTranscript()
        XCTAssertEqual(untimed.apply(duration: 2, text: "  what \n is  ", words: []).text, "what is")
    }

    // MARK: - Pause commit

    func testAPauseCommitsEveryWordAndKeepsTheLastThreeTenths() {
        var transcript = RollingTranscript()

        // "main" ends at 3.5 s and the window is 5 s long: a second and a half of quiet.
        let outcome = transcript.apply(duration: 5, text: "what is the main", words: question)
        assertOutcome(outcome, text: "what is the main", drop: 4.7, rule: .pause)
        XCTAssertEqual(transcript.committed, "what is the main")
        XCTAssertEqual(transcript.pending, "")
    }

    func testAPauseIsAtLeastTwelveTenthsOfASecond() {
        let words = [word("yes", 1.5, 2.0)]

        var short = RollingTranscript()
        assertOutcome(short.apply(duration: 3.1, text: "yes", words: words), text: "yes", drop: 0, rule: nil)
        XCTAssertEqual(short.committed, "")

        var exact = RollingTranscript()
        assertOutcome(exact.apply(duration: 3.2, text: "yes", words: words), text: "yes", drop: 2.9, rule: .pause)
        XCTAssertEqual(exact.committed, "yes")
    }

    func testWordsAfterAPauseFollowTheCommittedText() {
        var transcript = RollingTranscript()
        _ = transcript.apply(duration: 5, text: "what is the main", words: question)

        // The window now starts 0.3 s before the end of the old one, so the times start again.
        let outcome = transcript.apply(duration: 2, text: "advantage", words: [word("advantage", 0.5, 1.2)])
        assertOutcome(outcome, text: "what is the main advantage", drop: 0, rule: nil)
        XCTAssertEqual(transcript.committed, "what is the main")
        XCTAssertEqual(transcript.pending, "advantage")
    }

    func testAPauseComesBeforeEveryOtherRule() {
        var transcript = RollingTranscript()
        // A window past the hard cut, with a gap it could be cut at: the pause still decides.
        let words = [word("a", 1.0, 4.0), word("b", 6.0, 9.0), word("c", 10.0, 13.0)]
        let outcome = transcript.apply(duration: 14.5, text: "a b c", words: words)
        assertOutcome(outcome, text: "a b c", drop: 14.2, rule: .pause)
        XCTAssertEqual(transcript.committed, "a b c")
    }

    // MARK: - Silence trim

    func testSilenceLongerThanTwoSecondsKeepsHalfASecond() {
        var transcript = RollingTranscript()

        // Two seconds are not longer than two seconds.
        assertOutcome(transcript.apply(duration: 2, text: "", words: []), text: "", drop: 0, rule: nil)
        assertOutcome(transcript.apply(duration: 3, text: "", words: []), text: "", drop: 2.5, rule: .silence)
        // Text of blanks only is silence too.
        assertOutcome(transcript.apply(duration: 4, text: "  \n", words: []), text: "", drop: 3.5, rule: .silence)
        XCTAssertEqual(transcript.committed, "")
    }

    func testSilenceLeavesTheCommittedTextOnScreen() {
        var transcript = RollingTranscript()
        _ = transcript.apply(duration: 5, text: "what is the main", words: question)

        let outcome = transcript.apply(duration: 2.5, text: "", words: [])
        assertOutcome(outcome, text: "what is the main", drop: 2, rule: .silence)
        XCTAssertEqual(transcript.committed, "what is the main")
    }

    func testAPassThatHearsNothingDropsWhatWasOpen() {
        var transcript = RollingTranscript()
        _ = transcript.apply(duration: 1.5, text: "uh", words: [word("uh", 0.8, 1.2)])
        XCTAssertEqual(transcript.pending, "uh")

        // The recogniser changed its mind about the same audio: nothing was said.
        let outcome = transcript.apply(duration: 1.8, text: "", words: [])
        assertOutcome(outcome, text: "", drop: 0, rule: nil)
        XCTAssertEqual(transcript.pending, "")
        XCTAssertEqual(transcript.finish(), "")
    }

    // MARK: - Length commit

    func testALongWindowIsCommittedUpToItsWidestGap() {
        var transcript = RollingTranscript()

        let outcome = transcript.apply(duration: 11, text: "a b c d e f g h", words: longWindow)

        // Cut in the middle of the second between "d" (ends 5.0) and "e" (starts 6.0).
        assertOutcome(outcome, text: "a b c d e f g h", drop: 5.5, rule: .length)
        XCTAssertEqual(transcript.committed, "a b c d")
        XCTAssertEqual(transcript.pending, "e f g h")
    }

    func testAWindowShorterThanElevenSecondsIsNotCut() {
        var transcript = RollingTranscript()
        let outcome = transcript.apply(duration: 10.9, text: "a b c d e f g h", words: longWindow)
        assertOutcome(outcome, text: "a b c d e f g h", drop: 0, rule: nil)
        XCTAssertEqual(transcript.committed, "")
    }

    func testOfEqualGapsTheLaterOneIsTaken() {
        var transcript = RollingTranscript()
        // Three gaps of half a second, all of them usable.
        let words = [word("a", 1.0, 3.0), word("b", 3.5, 4.0), word("c", 4.5, 5.0), word("d", 5.5, 11.5)]

        let outcome = transcript.apply(duration: 12, text: "a b c d", words: words)

        assertOutcome(outcome, text: "a b c d", drop: 5.25, rule: .length)
        XCTAssertEqual(transcript.committed, "a b c")
        XCTAssertEqual(transcript.pending, "d")
    }

    func testAGapInTheFirstThreeOrTheLastTwoSecondsIsNotUsed() {
        var transcript = RollingTranscript()
        // The widest gap comes too early ("a" ends at 1.0), the next widest too late ("d" starts at 9.5,
        // less than two seconds before the end). The narrow one between them is the one that is left.
        let words = [word("a", 0.2, 1.0), word("b", 2.9, 5.0), word("c", 5.1, 8.9), word("d", 9.5, 10.6)]

        let outcome = transcript.apply(duration: 11, text: "a b c d", words: words)

        assertOutcome(outcome, text: "a b c d", drop: 5.05, rule: .length)
        XCTAssertEqual(transcript.committed, "a b")
        XCTAssertEqual(transcript.pending, "c d")
    }

    func testWordsAfterALengthCommitFollowTheCommittedText() {
        var transcript = RollingTranscript()
        _ = transcript.apply(duration: 11, text: "a b c d e f g h", words: longWindow)

        // 5.5 s went: the same words are heard again, 5.5 s earlier, and one more.
        let rest = [word("e", 0.5, 1.5), word("f", 1.7, 2.5), word("g", 3.0, 3.5), word("h", 4.1, 5.0), word("i", 5.4, 6.0)]
        let outcome = transcript.apply(duration: 6.5, text: "e f g h i", words: rest)
        assertOutcome(outcome, text: "a b c d e f g h i", drop: 0, rule: nil)
        XCTAssertEqual(transcript.committed, "a b c d")
    }

    // MARK: - Hard cut

    func testAFullWindowWithoutAUsableGapIsCommittedWhole() {
        // The one gap starts before the third second.
        let words = [word("a", 0.5, 2.5), word("b", 12.5, 13.5)]

        var almost = RollingTranscript()
        assertOutcome(almost.apply(duration: 13.9, text: "a b", words: words), text: "a b", drop: 0, rule: nil)
        XCTAssertEqual(almost.committed, "")

        var full = RollingTranscript()
        assertOutcome(full.apply(duration: 14, text: "a b", words: words), text: "a b", drop: 13.8, rule: .hardCut)
        XCTAssertEqual(full.committed, "a b")
        XCTAssertEqual(full.pending, "")
    }

    func testWithoutTimesTheTextIsCommittedOnlyWhole() {
        var transcript = RollingTranscript()

        // No times: no pause can be seen and there is no gap to cut at.
        assertOutcome(transcript.apply(duration: 5, text: "hello there", words: []), text: "hello there", drop: 0, rule: nil)
        assertOutcome(
            transcript.apply(duration: 12, text: "hello there general", words: []),
            text: "hello there general",
            drop: 0,
            rule: nil
        )
        XCTAssertEqual(transcript.committed, "")

        let outcome = transcript.apply(duration: 14.5, text: "hello there general kenobi", words: [])
        assertOutcome(outcome, text: "hello there general kenobi", drop: 14.3, rule: .hardCut)
        XCTAssertEqual(transcript.committed, "hello there general kenobi")
    }

    func testEveryPassOfAFullWindowLetsAudioGo() {
        // Whatever a pass over 14 seconds or more hears, the window gets shorter.
        let passes: [(text: String, words: [Word])] = [
            ("", []),
            ("untimed text", []),
            ("a b", [word("a", 0.5, 2.5), word("b", 12.5, 14.4)]),
            ("a b c d e f g h", longWindow)
        ]
        for pass in passes {
            var transcript = RollingTranscript()
            let outcome = transcript.apply(duration: 14.5, text: pass.text, words: pass.words)
            XCTAssertNotNil(outcome.rule, pass.text)
            XCTAssertGreaterThan(outcome.dropSeconds, 0, pass.text)
            XCTAssertLessThanOrEqual(outcome.dropSeconds, 14.5, pass.text)
        }
    }

    // MARK: - Finish and reset

    func testFinishCommitsWhatTheLastPassLeftOpen() {
        var transcript = RollingTranscript()
        _ = transcript.apply(duration: 5, text: "what is the main", words: question)
        _ = transcript.apply(duration: 2, text: "advantage of", words: [word("advantage", 0.5, 1.2), word("of", 1.3, 1.6)])
        XCTAssertEqual(transcript.committed, "what is the main")

        XCTAssertEqual(transcript.finish(), "what is the main advantage of")
        XCTAssertEqual(transcript.committed, "what is the main advantage of")
        XCTAssertEqual(transcript.pending, "")
        // Nothing is open any more: finishing again adds nothing.
        XCTAssertEqual(transcript.finish(), "what is the main advantage of")
    }

    func testResetClearsEverything() {
        var transcript = RollingTranscript()
        _ = transcript.apply(duration: 5, text: "what is the main", words: question)
        _ = transcript.apply(duration: 2, text: "advantage", words: [word("advantage", 0.5, 1.2)])

        transcript.reset()

        XCTAssertEqual(transcript, RollingTranscript())
        XCTAssertEqual(transcript.text, "")
        XCTAssertEqual(transcript.finish(), "")
    }
}
