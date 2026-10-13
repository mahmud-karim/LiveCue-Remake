import UIKit
import XCTest

/// The gate of design system 3.2: white text on the prominent session control needs a fill that is dark
/// enough. On iOS 26 the system draws that fill, so it is measured here in a screenshot. Before iOS 26
/// the control is a solid capsule whose colour is known, and the test skips itself.
final class GlassContrastUITests: XCTestCase {
    /// White text is at 4.5:1 or better on a fill of this relative luminance or less.
    private static let highestLuminance = 0.183

    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    @MainActor
    func testProminentControlFillIsDarkEnough() throws {
        guard #available(iOS 26.0, *) else {
            throw XCTSkip("Before iOS 26 the Start control is a solid capsule; there is no system material to measure.")
        }

        UITestWalk.launchToHome()
        let control = UITestSupport.waitFor("start-session")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(control), "Start is not enabled, so it does not show its fill.")
        UITestSupport.attachScreenshot(named: "glass-home", to: self)

        let picture = control.screenshot()
        let attachment = XCTAttachment(screenshot: picture)
        attachment.name = "glass-start-control"
        attachment.lifetime = .keepAlways
        add(attachment)

        // The label sits in the middle of the capsule. The fill is sampled left and right of it, in the
        // middle band of the height, away from the edge and its highlight.
        let pixels = try XCTUnwrap(Pixels(picture.image), "The picture of the control cannot be read.")
        let left = pixels.luminances(x: 0.10...0.18, y: 0.40...0.60)
        let right = pixels.luminances(x: 0.82...0.90, y: 0.40...0.60)
        let samples = (left + right).sorted()
        XCTAssertGreaterThan(samples.count, 100, "The picture of the control is too small to sample: \(pixels.width) by \(pixels.height).")

        let median = samples[samples.count / 2]
        let brightest = samples[samples.count - 1]
        print("WP12-NOTE: prominent fill luminance: median \(median), brightest \(brightest), \(samples.count) samples")
        XCTAssertLessThanOrEqual(
            median,
            GlassContrastUITests.highestLuminance,
            "The fill of the Start control has relative luminance \(median); white text needs \(GlassContrastUITests.highestLuminance) or less."
        )
        XCTAssertLessThanOrEqual(
            brightest,
            GlassContrastUITests.highestLuminance,
            "The brightest sampled point of the Start control's fill has relative luminance \(brightest)."
        )
    }
}

/// The pixels of a picture as sRGB bytes.
private struct Pixels {
    let width: Int
    let height: Int
    private let bytes: [UInt8]

    init?(_ image: UIImage) {
        guard let source = image.cgImage, let space = CGColorSpace(name: CGColorSpace.sRGB) else {
            return nil
        }
        let width = source.width
        let height = source.height
        guard width > 0, height > 0,
              let context = CGContext(
                  data: nil,
                  width: width,
                  height: height,
                  bitsPerComponent: 8,
                  bytesPerRow: width * 4,
                  space: space,
                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
              )
        else {
            return nil
        }
        context.draw(source, in: CGRect(x: 0, y: 0, width: width, height: height))
        guard let data = context.data else {
            return nil
        }
        let buffer = UnsafeBufferPointer(start: data.assumingMemoryBound(to: UInt8.self), count: width * height * 4)
        self.width = width
        self.height = height
        self.bytes = Array(buffer)
    }

    /// The relative luminance (WCAG 2) of every second pixel in a part of the picture, given as shares
    /// of its width and height.
    func luminances(x: ClosedRange<Double>, y: ClosedRange<Double>) -> [Double] {
        let columns = stride(from: Int(Double(width) * x.lowerBound), to: Int(Double(width) * x.upperBound), by: 2)
        let rows = stride(from: Int(Double(height) * y.lowerBound), to: Int(Double(height) * y.upperBound), by: 2)
        var values: [Double] = []
        for row in rows {
            for column in columns {
                let index = (row * width + column) * 4
                let red = Pixels.linear(bytes[index])
                let green = Pixels.linear(bytes[index + 1])
                let blue = Pixels.linear(bytes[index + 2])
                values.append(0.2126 * red + 0.7152 * green + 0.0722 * blue)
            }
        }
        return values
    }

    /// One sRGB channel as linear light.
    private static func linear(_ byte: UInt8) -> Double {
        let channel = Double(byte) / 255
        if channel <= 0.04045 {
            return channel / 12.92
        }
        return pow((channel + 0.055) / 1.055, 2.4)
    }
}
