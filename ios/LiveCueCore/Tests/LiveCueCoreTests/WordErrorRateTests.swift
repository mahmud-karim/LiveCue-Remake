import Foundation
import LiveCueCore
import XCTest

/// Word error rate.
final class WordErrorRateTests: XCTestCase {
    /// AC-CORE-012 and AC-CORE-013.
    func testWordErrorRate() {
        XCTAssertEqual(WordErrorRate.compute(reference: "one two three", hypothesis: "one too three"), 1.0 / 3.0, accuracy: 0.001)
        XCTAssertEqual(WordErrorRate.compute(reference: "same words", hypothesis: "same words"), 0)
    }

    /// AC-CORE-S20.
    func testAnEmptyReferenceGivesZeroForAnEmptyHypothesisAndOneOtherwise() {
        XCTAssertEqual(WordErrorRate.compute(reference: "", hypothesis: ""), 0)
        XCTAssertEqual(WordErrorRate.compute(reference: "", hypothesis: "anything"), 1)
        XCTAssertEqual(WordErrorRate.compute(reference: "", hypothesis: "several words at once"), 1)

        // Text without a letter or a digit has no words.
        XCTAssertEqual(WordErrorRate.compute(reference: " ... !? ", hypothesis: "  \n"), 0)
        XCTAssertEqual(WordErrorRate.compute(reference: "?!", hypothesis: "word"), 1)
        XCTAssertEqual(WordErrorRate.compute(reference: "", hypothesis: "--"), 0)
    }

    /// AC-CORE-S20.
    func testComparisonIgnoresCaseAndPunctuation() {
        XCTAssertEqual(WordErrorRate.compute(reference: "Hello, World!", hypothesis: "hello world"), 0)
        XCTAssertEqual(WordErrorRate.compute(reference: "ONE TWO THREE", hypothesis: "one two three"), 0)
        XCTAssertEqual(WordErrorRate.compute(reference: "one-two;three", hypothesis: "one two three"), 0)
        XCTAssertEqual(WordErrorRate.compute(reference: "  one\ttwo\nthree  ", hypothesis: "one two three"), 0)
        XCTAssertEqual(WordErrorRate.compute(reference: "\u{C9}t\u{E9}", hypothesis: "\u{E9}T\u{C9}"), 0, "Case is folded beyond ASCII.")
    }

    func testEveryCharacterThatIsNotALetterOrADigitSplitsWords() {
        // "don't" is the two words "don" and "t".
        XCTAssertEqual(WordErrorRate.compute(reference: "don't stop", hypothesis: "don t stop"), 0)
        XCTAssertEqual(WordErrorRate.compute(reference: "don't stop", hypothesis: "dont stop"), 2.0 / 3.0, accuracy: 0.000_001)
        // Digits are words.
        XCTAssertEqual(WordErrorRate.compute(reference: "room 42", hypothesis: "room 42"), 0)
        XCTAssertEqual(WordErrorRate.compute(reference: "room 42", hypothesis: "room 24"), 0.5)
        XCTAssertEqual(WordErrorRate.compute(reference: "3.5 percent", hypothesis: "3 5 percent"), 0)
    }

    func testEachInsertionDeletionAndSubstitutionCostsOne() {
        // One deletion out of four words.
        XCTAssertEqual(WordErrorRate.compute(reference: "the quick brown fox", hypothesis: "the quick fox"), 0.25)
        // One insertion.
        XCTAssertEqual(WordErrorRate.compute(reference: "the quick brown fox", hypothesis: "the very quick brown fox"), 0.25)
        // One substitution.
        XCTAssertEqual(WordErrorRate.compute(reference: "the quick brown fox", hypothesis: "the quick brown dog"), 0.25)
        // A substitution and a deletion.
        XCTAssertEqual(WordErrorRate.compute(reference: "the quick brown fox", hypothesis: "a quick fox"), 0.5)
        // Everything deleted.
        XCTAssertEqual(WordErrorRate.compute(reference: "the quick brown fox", hypothesis: ""), 1)
        // Word order matters: a swap is two substitutions.
        XCTAssertEqual(WordErrorRate.compute(reference: "one two", hypothesis: "two one"), 1)
        // The classic pair: kitten to sitting is three edits, here on letters written as words.
        XCTAssertEqual(
            WordErrorRate.compute(reference: "k i t t e n", hypothesis: "s i t t i n g"),
            3.0 / 6.0
        )
    }

    func testTheRateCanExceedOne() {
        XCTAssertEqual(WordErrorRate.compute(reference: "one", hypothesis: "four five six"), 3)
        XCTAssertEqual(WordErrorRate.compute(reference: "one", hypothesis: "one two three"), 2)
        XCTAssertEqual(WordErrorRate.compute(reference: "one two", hypothesis: "three four five six seven"), 2.5)
    }

    func testTheRateIsMeasuredAgainstTheReference() {
        // The same pair the other way round has a different denominator.
        XCTAssertEqual(WordErrorRate.compute(reference: "one two three four", hypothesis: "one two"), 0.5)
        XCTAssertEqual(WordErrorRate.compute(reference: "one two", hypothesis: "one two three four"), 1)
    }

    func testALongTranscript() {
        let reference = Array(repeating: "alpha beta gamma delta", count: 100).joined(separator: " ")
        XCTAssertEqual(WordErrorRate.compute(reference: reference, hypothesis: reference), 0)
        // Every fourth word is wrong.
        let hypothesis = Array(repeating: "alpha beta gamma epsilon", count: 100).joined(separator: " ")
        XCTAssertEqual(WordErrorRate.compute(reference: reference, hypothesis: hypothesis), 0.25)
    }
}
