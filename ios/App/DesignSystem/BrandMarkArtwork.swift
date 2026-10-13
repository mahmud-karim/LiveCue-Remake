import SwiftUI

/// The geometry of the mark, from the master artwork of design system 8.2: a speech bubble holding three
/// voice bars. The numbers are positions on the 1024 by 1024 master canvas and are the artwork itself;
/// `BrandMark` paints these shapes with theme colours. The app icon is generated from the same numbers by
/// `scripts/generate_app_icon.py`.
enum BrandArtwork {
    /// The box the glyph is drawn in: as wide as the bubble, and as tall as the bubble with its tail.
    static let box = CGRect(x: 232, y: 222, width: 560, height: 600)

    static let bubble = CGRect(x: 232, y: 222, width: 560, height: 460)
    static let bubbleRadius: CGFloat = 150

    static let barWidth: CGFloat = 68
    static let barCenterY: CGFloat = 452
    /// Centre and height of each bar. Uneven on purpose: short, tall, medium reads as a voice.
    static let bars: [(x: CGFloat, height: CGFloat)] = [(392, 150), (512, 270), (632, 200)]

    static let gloss = CGRect(x: 182, y: 160, width: 500, height: 260)
    static let glossAngle = Angle(degrees: -12)

    /// The rim is stroked this wide inside the bubble's outline.
    static let rim: CGFloat = 6

    static var aspectRatio: CGFloat {
        box.width / box.height
    }

    /// Where the gloss gradient starts and ends, in the unit space of the box.
    static var glossStart: UnitPoint {
        UnitPoint(x: 0.5, y: (gloss.minY - box.minY) / box.height)
    }

    static var glossEnd: UnitPoint {
        UnitPoint(x: 0.5, y: (gloss.maxY - box.minY) / box.height)
    }

    /// The width of a line that is `length` wide on the master canvas, in a mark of this height.
    static func scaled(_ length: CGFloat, toHeight height: CGFloat) -> CGFloat {
        length * height / box.height
    }

    /// Maps the box onto `rect`, centred and at one scale in both directions.
    static func transform(into rect: CGRect) -> CGAffineTransform {
        let scale = min(rect.width / box.width, rect.height / box.height)
        return CGAffineTransform(translationX: rect.midX - box.midX * scale, y: rect.midY - box.midY * scale)
            .scaledBy(x: scale, y: scale)
    }
}

/// The bubble: a rounded rectangle united with its tail.
struct BrandBubbleShape: Shape {
    func path(in rect: CGRect) -> Path {
        var tail = Path()
        tail.move(to: CGPoint(x: 330, y: 640))
        tail.addLine(to: CGPoint(x: 296, y: 800))
        tail.addCurve(
            to: CGPoint(x: 320, y: 816),
            control1: CGPoint(x: 292, y: 818),
            control2: CGPoint(x: 306, y: 826)
        )
        tail.addLine(to: CGPoint(x: 508, y: 680))
        tail.closeSubpath()

        let body = Path(roundedRect: BrandArtwork.bubble, cornerRadius: BrandArtwork.bubbleRadius, style: .circular)
        return body.union(tail).applying(BrandArtwork.transform(into: rect))
    }
}

/// The three voice bars.
struct BrandBarsShape: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        for bar in BrandArtwork.bars {
            let box = CGRect(
                x: bar.x - BrandArtwork.barWidth / 2,
                y: BrandArtwork.barCenterY - bar.height / 2,
                width: BrandArtwork.barWidth,
                height: bar.height
            )
            path.addRoundedRect(
                in: box,
                cornerSize: CGSize(width: BrandArtwork.barWidth / 2, height: BrandArtwork.barWidth / 2),
                style: .circular
            )
        }
        return path.applying(BrandArtwork.transform(into: rect))
    }
}

/// The tilted ellipse of the gloss. `BrandMark` clips it to the bubble.
struct BrandGlossShape: Shape {
    func path(in rect: CGRect) -> Path {
        let gloss = BrandArtwork.gloss
        let turn = CGAffineTransform(translationX: gloss.midX, y: gloss.midY)
            .rotated(by: CGFloat(BrandArtwork.glossAngle.radians))
            .translatedBy(x: -gloss.midX, y: -gloss.midY)
        return Path(ellipseIn: gloss)
            .applying(turn)
            .applying(BrandArtwork.transform(into: rect))
    }
}
