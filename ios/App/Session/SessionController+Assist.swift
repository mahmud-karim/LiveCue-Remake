import Foundation
import LiveCueCore

/// A failure the pipeline finds by itself, with the text of the card.
private struct AssistStop: Error {
    let message: String
}

/// The Assist pipeline and its retry (section 4.3).
extension SessionController {
    func assist() async {
        await runAssist(reusingText: false)
    }

    func retrySameText() async {
        await runAssist(reusingText: true)
    }

    /// The thirteen steps of section 4.3. All clocks are `clock.uptime()`.
    private func runAssist(reusingText: Bool) async {
        // T-21: an Assist needs a session, and nothing else may be under way.
        guard let started = session, !isAssisting, !isTransitioning else { return }
        // A retry with nothing remembered runs as fresh.
        let remembered = reusingText ? rememberedAssist : nil
        let isRetry = remembered != nil

        // Step 1
        clearAssistCard()

        // Step 2
        let selection = dependencies.assistant.configuration
        if let message = unmetPrecondition(of: selection.provider) {
            showAssistPrecondition(message)
            return
        }

        // Step 3
        let clock = dependencies.clock
        let startedAt = clock.uptime()
        let sessionID = started.id
        let instruction = dependencies.assistant.instruction
        let speechProvider = provider
        let transcribesFirst = speechProvider == .voz && !isRetry
        beginAssist(stage: transcribesFirst ? .transcribing : .thinking)

        do {
            // Step 4
            var transcriptionMs: Double = 0
            if transcribesFirst {
                transcriptionMs = try await transcribePendingAudio() * 1_000
            }

            // Step 5
            var request: AssistRequest
            let marker: UUID?
            if let remembered {
                request = ContextBuilder.retry(remembered.request, assistant: selection)
                marker = remembered.marker
            } else {
                guard let current = session, current.id == sessionID else {
                    throw CancellationError()
                }
                request = ContextBuilder.assistRequest(session: current, partial: partialText, instruction: instruction)
                guard ContextBuilder.hasContent(request) else {
                    throw AssistStop(message: SessionTexts.noSpeech)
                }
                marker = current.segments.last?.id
            }

            // Step 6
            setAssistStage(.checkingModel)
            let service = dependencies.answers.service(for: selection.provider)
            let catalogue = try await catalogueForCheck(of: service)

            // Step 7: the selection is never changed.
            guard catalogue.supports(selection) else {
                switch selection.provider {
                case .codex:
                    throw AssistStop(message: SessionTexts.selectionNotInCatalogue(selection))
                case .openrouter:
                    throw OpenRouterError.noModel
                }
            }

            // Step 8
            let contextMs = max(0, (clock.uptime() - startedAt) * 1_000 - transcriptionMs)
            setAssistStage(.thinking)
            request.assistant = selection
            request = try WireBudget.fitted(request)
            rememberAssist(request, marker: marker)

            // Step 9
            let sentAt = clock.uptime()
            let response = try await service.answer(request, onProgress: { [weak self] text in
                await self?.showStreamedAnswer(text, forSession: sessionID)
            })
            let finishedAt = clock.uptime()

            // Steps 10 and 11
            let performance = AssistPerformance(
                configuration: selection,
                speechModel: speechProvider?.rawValue ?? "",
                reusedText: isRetry,
                transcriptionMs: transcriptionMs,
                contextMs: contextMs,
                roundTripMs: max(0, (finishedAt - sentAt) * 1_000),
                totalMs: max(0, (finishedAt - startedAt) * 1_000),
                lastLiveChunkMs: speechProvider == .parakeet ? lastLiveChunkMilliseconds : nil,
                transcriptCharacters: request.transcript.count + (request.partialTranscript?.count ?? 0),
                execution: response.execution
            )
            let turn = AssistantTurn(
                createdAt: clock.now(),
                request: request.instruction,
                detectedQuestion: response.detectedQuestion,
                answer: response.answer,
                details: response.details,
                performance: performance
            )
            // The app owns the marker for both services: a reply never supplies it.
            var memory = response.memory
            memory.throughSegmentId = marker
            saveAnswer(turn, memory: memory, forSession: sessionID)
        } catch let stop as AssistStop {
            // Step 12
            failAssist(stop.message)
        } catch {
            failAssist(AnswerErrorText.message(for: error, provider: selection.provider))
        }

        // Step 13
        endAssist()
    }

    /// Step 2. The text of the card when the selected service cannot be asked at all.
    private func unmetPrecondition(of answers: AssistantProvider) -> String? {
        switch answers {
        case .codex:
            guard dependencies.pairingStore.load() == nil else { return nil }
            return RelayMessages.text(for: RelayLocalCondition.notPaired)
        case .openrouter:
            guard dependencies.secrets.read(.openRouterAPIKey) == nil else { return nil }
            return AnswerErrorText.message(for: OpenRouterError.missingKey, provider: .openrouter)
        }
    }

    /// Step 6. Codex: the PC's catalogue, fetched every time. OpenRouter: the loaded catalogue, fetched
    /// and stored only when there is none; a failed fetch fails the Assist with its own text (IOS-B9).
    private func catalogueForCheck(of service: any AnswerService) async throws -> [AssistantModelOption] {
        switch service.provider {
        case .codex:
            return try await service.loadCatalogue()
        case .openrouter:
            let loaded = dependencies.assistant.catalogue
            if !loaded.isEmpty {
                return loaded
            }
            let fetched = try await service.loadCatalogue()
            dependencies.assistant.adoptCatalogue(fetched)
            return fetched
        }
    }
}
