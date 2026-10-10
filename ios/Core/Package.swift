// swift-tools-version: 6.0
import PackageDescription

// The platform-independent core. It may import Foundation and nothing else, so that
// `swift build` and `swift test` work on Linux as well as in Xcode.
let package = Package(
    name: "LiveCueCore",
    platforms: [.iOS(.v18), .macOS(.v15)],
    products: [
        .library(name: "LiveCueCore", targets: ["LiveCueCore"])
    ],
    targets: [
        .target(name: "LiveCueCore"),
        .testTarget(name: "LiveCueCoreTests", dependencies: ["LiveCueCore"])
    ]
)
