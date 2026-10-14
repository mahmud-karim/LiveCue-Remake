import XCTest
@testable import LiveCueRemake

/// Each of the fourteen arguments sets its flag; `-ui-testing` and the unit-test host select fixture
/// mode; anything else is live.
final class LaunchConfigurationTests: XCTestCase {
    private let modifiers: [(argument: String, isSet: (TestMode) -> Bool)] = [
        ("-on-device-ready", { $0.onDeviceReady }),
        ("-fluid-ready", { $0.fluidReady }),
        ("-meta-no-key", { $0.metaNoKey }),
        ("-meta-key-persistence", { $0.metaKeyPersistence }),
        ("-openrouter-ui-reset", { $0.openRouterUIReset }),
        ("-openrouter-persistence-test", { $0.openRouterPersistenceTest }),
        ("-pc-offline", { $0.pcOffline }),
        ("-pc-catalog-slow", { $0.pcCatalogSlow }),
        ("-pc-model-slow-start", { $0.pcModelSlowStart }),
        ("-pc-model-start-error", { $0.pcModelStartError }),
        ("-assist-fails-once", { $0.assistFailsOnce }),
        ("-pairing-persistence-test", { $0.pairingPersistenceTest }),
        ("-save-pairing-fixture", { $0.savePairingFixture }),
        ("-drop-test-keychain", { $0.dropTestKeychain })
    ]

    @MainActor
    func testThereAreFourteenModifiers() async {
        XCTAssertEqual(modifiers.count, 14)
        XCTAssertEqual(Set(modifiers.map(\.argument)).count, 14)
    }

    @MainActor
    func testBaseTestModeSetsNoFlag() async {
        let base = TestMode()
        for modifier in modifiers {
            XCTAssertFalse(modifier.isSet(base), modifier.argument)
        }
        XCTAssertEqual(TestMode(arguments: []), base)
        XCTAssertEqual(TestMode(arguments: ["LiveCueRemake", "-ui-testing", "-unknown-argument"]), base)
    }

    @MainActor
    func testEachArgumentSetsItsOwnFlagAndNoOther() async {
        for modifier in modifiers {
            let mode = TestMode(arguments: ["LiveCueRemake", "-ui-testing", modifier.argument])
            XCTAssertTrue(modifier.isSet(mode), "\(modifier.argument) did not set its flag")
            for other in modifiers where other.argument != modifier.argument {
                XCTAssertFalse(other.isSet(mode), "\(modifier.argument) also set the flag of \(other.argument)")
            }
            XCTAssertNotEqual(mode, TestMode())
        }
    }

    @MainActor
    func testAllArgumentsTogetherSetEveryFlag() async {
        let mode = TestMode(arguments: modifiers.map(\.argument))
        for modifier in modifiers {
            XCTAssertTrue(modifier.isSet(mode), modifier.argument)
        }
    }

    @MainActor
    func testUITestingArgumentSelectsFixtureModeWithItsModifiers() async {
        let base = LaunchConfiguration(arguments: ["LiveCueRemake", "-ui-testing"], environment: [:])
        XCTAssertEqual(base.mode, .fixture(TestMode()))

        var expected = TestMode()
        expected.pcOffline = true
        expected.assistFailsOnce = true
        let modified = LaunchConfiguration(
            arguments: ["LiveCueRemake", "-ui-testing", "-pc-offline", "-assist-fails-once"],
            environment: [:]
        )
        XCTAssertEqual(modified.mode, .fixture(expected))
    }

    @MainActor
    func testUnitTestHostSelectsBaseTestMode() async {
        let hosted = LaunchConfiguration(
            arguments: ["LiveCueRemake"],
            environment: ["XCTestConfigurationFilePath": "/tmp/session.xctestconfiguration"]
        )
        XCTAssertEqual(hosted.mode, .fixture(TestMode()))

        // Without `-ui-testing` a modifier means nothing: the host runs in base test mode.
        let withModifier = LaunchConfiguration(
            arguments: ["LiveCueRemake", "-pc-offline"],
            environment: ["XCTestConfigurationFilePath": ""]
        )
        XCTAssertEqual(withModifier.mode, .fixture(TestMode()))
    }

    @MainActor
    func testAnythingElseIsLive() async {
        XCTAssertEqual(LaunchConfiguration(arguments: [], environment: [:]).mode, .live)
        XCTAssertEqual(LaunchConfiguration(arguments: ["LiveCueRemake"], environment: ["HOME": "/"]).mode, .live)
        // A modifier alone, a different spelling and a different case do not switch the seam on.
        XCTAssertEqual(LaunchConfiguration(arguments: ["LiveCueRemake", "-pc-offline"], environment: [:]).mode, .live)
        XCTAssertEqual(LaunchConfiguration(arguments: ["LiveCueRemake", "--ui-testing"], environment: [:]).mode, .live)
        XCTAssertEqual(LaunchConfiguration(arguments: ["LiveCueRemake", "-UI-TESTING"], environment: [:]).mode, .live)
        XCTAssertEqual(LaunchConfiguration(arguments: ["LiveCueRemake", "ui-testing"], environment: [:]).mode, .live)
    }

    /// These tests run inside the app, so this is the app's own launch: Xcode must have marked the
    /// process as a unit-test host, or the app would have started on its live dependencies.
    @MainActor
    func testTheAppThatHostsTheseTestsRunsInFixtureMode() async {
        XCTAssertEqual(LaunchConfiguration.current.mode, .fixture(TestMode()))
    }
}
