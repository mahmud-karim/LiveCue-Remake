import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Reading a payload, and verify and pair.
final class PairingControllerTests: XCTestCase {
    private let connectedMessage = "Connected. Your pairing is saved on this iPhone."
    /// The suffix of a Tailscale name. Hosts that end in it are put together at run time.
    private let tailscaleSuffix = ".ts.net"

    /// The pairing with another PC than the fixture's, on the same token.
    private func pairing(address: String) throws -> PairingPayload {
        try PairingPayload.manual(address: address, token: FixtureValues.pairingToken)
    }

    // MARK: - Reading a payload (AC-IOS-128, 129)

    @MainActor
    func testReadingAPayloadSavesAndSendsNothing() async throws {
        let owners = TestOwners(paired: false)

        let read = try owners.pairing.readPayload(FixtureValues.pairing.storedText)

        XCTAssertEqual(read, FixtureValues.pairing)
        XCTAssertEqual(read.endpoint, "https://pc.example.test:11443/")
        XCTAssertEqual(read.token, FixtureValues.pairingToken)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertNil(owners.pairingStore.load())
        XCTAssertFalse(owners.relay.isPaired)
        XCTAssertFalse(owners.pairing.isVerifying)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    @MainActor
    func testAPayloadThatIsNotValidThrowsThePayloadError() async {
        let owners = TestOwners(paired: false)
        let token = FixtureValues.pairingToken
        let invalid = [
            "",
            "not a pairing payload",
            "{\"endpoint\":\"http://pc.example.test/\",\"token\":\"\(token)\"}",
            "{\"endpoint\":\"https://pc.example.test/\",\"token\":\"short\"}",
            "{\"endpoint\":\"https://pc.example.test/\"}"
        ]

        for text in invalid {
            XCTAssertThrowsError(try owners.pairing.readPayload(text), text) { error in
                XCTAssertEqual(error as? PairingPayloadError, .invalid, text)
                XCTAssertEqual(
                    (error as? PairingPayloadError)?.errorDescription,
                    "Use the pairing QR from LiveCue Desktop. It must contain an HTTPS PC address and a valid pairing token."
                )
            }
        }

        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    @MainActor
    func testVerifyingInvalidInputThrowsThePayloadErrorAndSendsNothing() async {
        let owners = TestOwners()
        let token = FixtureValues.pairingToken
        let inputs: [(address: String, token: String)] = [
            ("", token),
            ("pc.example.test", token),
            ("http://pc.example.test:11443/", token),
            ("https://pc.example.test/?query=1", token),
            ("https://pc.example.test:11443/", ""),
            ("https://pc.example.test:11443/", "short"),
            ("https://pc.example.test:11443/", token + "\n"),
            ("https://pc.example.test:11443/", " " + token)
        ]

        for input in inputs {
            do {
                try await owners.pairing.verifyAndPair(address: input.address, token: input.token)
                XCTFail("Accepted the address \(input.address).")
            } catch let error as PairingPayloadError {
                XCTAssertEqual(error, .invalid)
            } catch {
                XCTFail("Unexpected error: \(error)")
            }
        }

        XCTAssertFalse(owners.pairing.isVerifying)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertEqual(owners.relay.pairing, FixtureValues.pairing, "The earlier pairing is untouched.")
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    // MARK: - Verify and pair (AC-IOS-130, 131)

    @MainActor
    func testASuccessfulVerificationSavesThePairingAndTellsTheMonitor() async throws {
        let owners = TestOwners(paired: false)
        let expected = FixtureValues.pairing
        await owners.gateway.setHold(true, for: "verifyPairing")

        // Manual entry: surrounding spaces and the letter case of scheme and host do not matter.
        let verification = Task {
            try await owners.pairing.verifyAndPair(address: " HTTPS://PC.Example.Test:11443 ", token: FixtureValues.pairingToken)
        }
        await owners.gateway.waitForCalls(count: 1)
        XCTAssertTrue(owners.pairing.isVerifying)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty, "Nothing is saved before the PC confirms.")
        XCTAssertFalse(owners.relay.isPaired)

        // A second tap while one verification runs sends nothing more.
        do {
            try await owners.pairing.verifyAndPair(address: "https://pc.example.test:11443/", token: FixtureValues.pairingToken)
            XCTFail("A second verification ran beside the first.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }

        await owners.gateway.setHold(false, for: "verifyPairing")
        await owners.gateway.release("verifyPairing")
        try await verification.value

        XCTAssertFalse(owners.pairing.isVerifying)
        XCTAssertEqual(owners.pairingStore.saved, [expected])
        XCTAssertEqual(owners.pairingStore.load(), expected)
        XCTAssertEqual(owners.relay.pairing, expected)
        XCTAssertTrue(owners.relay.isPaired)
        XCTAssertTrue(owners.relay.isOnline)
        XCTAssertEqual(owners.relay.savedPCState, .connected)
        XCTAssertEqual(owners.relay.connectionMessage, connectedMessage)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["verifyPairing"])
        let pairings = await owners.gateway.pairings
        XCTAssertEqual(pairings, [expected], "Route 2 was asked with the canonical endpoint and the token as typed.")
    }

    @MainActor
    func testAFailedVerificationLeavesTheEarlierPairing() async throws {
        let owners = TestOwners()
        let earlier = FixtureValues.pairing
        await owners.relay.checkHealthNow()
        XCTAssertEqual(owners.relay.savedPCState, .connected)

        let failures = [
            RelayFailure.wire(.unauthorized),
            RelayFailure.local(.unreachable),
            RelayFailure.local(.timeout)
        ]
        for failure in failures {
            await owners.gateway.setVerify(.failure(failure))
            do {
                try await owners.pairing.verifyAndPair(address: "https://other.example.test:11443/", token: FixtureValues.pairingToken)
                XCTFail("The verification did not fail.")
            } catch let thrown as RelayFailure {
                XCTAssertEqual(thrown, failure)
                // IOS-B10: the alert shows this text, and it never starts with the saved-pairing prefix.
                XCTAssertFalse(RelayMessages.text(for: thrown).hasPrefix("Your pairing is saved."))
            } catch {
                XCTFail("Unexpected error: \(error)")
            }
            XCTAssertFalse(owners.pairing.isVerifying)
        }

        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertEqual(owners.pairingStore.load(), earlier)
        XCTAssertEqual(owners.relay.pairing, earlier)
        XCTAssertTrue(owners.relay.isOnline, "The state of the saved PC is not touched by a failed pairing.")
        XCTAssertEqual(owners.relay.savedPCState, .connected)
        XCTAssertEqual(owners.relay.connectionMessage, connectedMessage)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "verifyPairing", "verifyPairing", "verifyPairing"], "One request for each tap.")
    }

    @MainActor
    func testAPairingThatCannotBeSavedLeavesTheEarlierPairing() async throws {
        let owners = TestOwners()
        owners.pairingStore.saveFails = true

        do {
            try await owners.pairing.verifyAndPair(address: "https://other.example.test:11443/", token: FixtureValues.pairingToken)
            XCTFail("The pairing was reported as saved.")
        } catch {
            XCTAssertFalse(error is RelayFailure, "The PC accepted the pairing. The failure is the store's.")
            XCTAssertFalse(error is PairingPayloadError)
        }

        XCTAssertFalse(owners.pairing.isVerifying)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertEqual(owners.pairingStore.load(), FixtureValues.pairing)
        XCTAssertEqual(owners.relay.pairing, FixtureValues.pairing)
        XCTAssertFalse(owners.relay.isOnline, "The monitor was not told about a pairing that was not saved.")
    }

    @MainActor
    func testPairingAgainReplacesTheSavedPairing() async throws {
        let owners = TestOwners()
        let other = try pairing(address: "https://other.example.test:11443/")

        try await owners.pairing.verifyAndPair(address: other.endpoint, token: other.token)

        XCTAssertEqual(owners.pairingStore.saved, [other])
        XCTAssertEqual(owners.relay.pairing, other)
        XCTAssertEqual(owners.relay.savedPCState, .connected)
    }

    // MARK: - Replacing a saved PC (ST-18)

    @MainActor
    func testThereIsNoReplacementWithNothingSaved() async {
        let owners = TestOwners(paired: false)

        XCTAssertNil(owners.pairing.replacement(forAddress: "https://other.example.test:11443/"))
        XCTAssertNil(owners.pairing.replacement(forAddress: "https://pc.example.test:11443/"))
        XCTAssertNil(owners.pairing.replacement(forAddress: "not an address"))
    }

    @MainActor
    func testThereIsNoReplacementForTheSavedHost() async {
        let owners = TestOwners()
        XCTAssertEqual(owners.pairingStore.load()?.relayEndpoint.host, "pc.example.test")

        let sameHost = [
            "https://pc.example.test:11443/",
            "https://pc.example.test:11443",
            "  https://pc.example.test:11443/  ",
            // Another port, another path.
            "https://pc.example.test/",
            "https://pc.example.test:10000/livecue/",
            // Another letter case.
            "https://PC.Example.TEST:11443/",
            "HTTPS://pc.example.test:11443/"
        ]
        for address in sameHost {
            XCTAssertNil(owners.pairing.replacement(forAddress: address), address)
        }
    }

    @MainActor
    func testAnotherHostGivesAReplacementThatNamesBothHosts() async {
        let owners = TestOwners()

        let plain = owners.pairing.replacement(forAddress: " HTTPS://Other.Example.Test:10000/livecue ")
        XCTAssertEqual(plain, PairingReplacement(currentHost: "pc.example.test", newHost: "other.example.test"))
        XCTAssertEqual(plain?.newHostIsTailscaleName, false)

        let tailscaleHost = "desk.tailnet" + tailscaleSuffix
        let tailscale = owners.pairing.replacement(forAddress: "https://" + tailscaleHost + ":11443/")
        XCTAssertEqual(tailscale, PairingReplacement(currentHost: "pc.example.test", newHost: tailscaleHost))
        XCTAssertEqual(tailscale?.newHostIsTailscaleName, true)

        // A name that only contains the suffix is not a Tailscale name.
        let lookalike = owners.pairing.replacement(forAddress: "https://desk" + tailscaleSuffix + ".example.test/")
        XCTAssertEqual(lookalike?.newHost, "desk" + tailscaleSuffix + ".example.test")
        XCTAssertEqual(lookalike?.newHostIsTailscaleName, false)

        // A subdomain of the saved host is another host.
        XCTAssertEqual(
            owners.pairing.replacement(forAddress: "https://relay.pc.example.test/")?.newHost,
            "relay.pc.example.test"
        )
    }

    @MainActor
    func testThereIsNoReplacementForAnAddressThatDoesNotParse() async {
        let owners = TestOwners()
        let unreadable = [
            "",
            "other.example.test",
            "http://other.example.test/",
            "https://other.example.test/?query=1",
            "https://other.example.test:0/",
            "https://other.example.test:99999/",
            "https://user@other.example.test/",
            "https://other.example.test/a b/"
        ]
        for address in unreadable {
            XCTAssertNil(owners.pairing.replacement(forAddress: address), address)
        }
    }

    @MainActor
    func testAskingForAReplacementSendsAndSavesNothing() async {
        let owners = TestOwners()

        _ = owners.pairing.replacement(forAddress: "https://other.example.test:11443/")
        _ = owners.pairing.replacement(forAddress: "https://pc.example.test:11443/")
        _ = owners.pairing.replacement(forAddress: "not an address")

        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertEqual(owners.pairingStore.load(), FixtureValues.pairing)
        XCTAssertEqual(owners.relay.pairing, FixtureValues.pairing)
        XCTAssertFalse(owners.pairing.isVerifying)
        XCTAssertFalse(owners.relay.isOnline)
        XCTAssertEqual(owners.relay.savedPCState, .reconnecting)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }
}
