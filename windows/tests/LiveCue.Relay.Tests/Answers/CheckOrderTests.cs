using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// The check order R-1 to R-7 of protocol 8.2 (statement P2-063): when two checks would fail, the
/// earlier one answers.
/// </summary>
public sealed class CheckOrderTests
{
    [Theory]
    [InlineData(AnswerTesting.Assist)]
    [InlineData(AnswerTesting.Summary)]
    public async Task The_four_orderings_hold(string route)
    {
        await using var host = await RelayTestHost.StartAsync();

        // One request holds the slot for the first two orderings.
        var gate = host.Backend.Gate = AnswerTesting.NewGate();
        var holder = host.PostAsync(AnswerTesting.Assist, Bodies.Assist("holds-the-slot"));
        await AnswerTesting.RunsStartedAsync(host, 1);

        // R-2 before R-3: slot busy and invalid JSON gives ASSISTANT_BUSY.
        using (var busyAndBroken = await host.PostAsync(route, "{"))
        {
            await AnswerTesting.AssertErrorAsync(busyAndBroken, RelayErrors.AssistantBusy);
        }

        // R-1 before R-2: paused and slot busy gives RELAY_PAUSED.
        await host.Relay.SetAcceptingAsync(false);
        using (var pausedAndBusy = await host.PostAsync(route, Bodies.Assist("second")))
        {
            await AnswerTesting.AssertErrorAsync(pausedAndBusy, RelayErrors.RelayPaused);
        }

        await host.Relay.SetAcceptingAsync(true);
        gate.SetResult();
        (await holder).Dispose();
        host.Backend.Gate = null;

        // R-3 before R-4: invalid JSON and no requestId gives INVALID_JSON.
        using (var brokenAndNoId = await host.PostAsync(route, "[]"))
        {
            await AnswerTesting.AssertErrorAsync(brokenAndNoId, RelayErrors.InvalidJson);
        }

        // R-4 before R-5: invalid requestId and invalid assistant gives INVALID_REQUEST_ID.
        using (var badIdAndBadAssistant = await host.PostAsync(route, """{"requestId":"a/b","assistant":"gpt-test"}"""))
        {
            await AnswerTesting.AssertErrorAsync(badIdAndBadAssistant, RelayErrors.InvalidRequestId);
        }

        // R-5 before R-6: malformed assistant and failing catalogue gives INVALID_ASSISTANT, and the
        // catalogue is not even asked.
        host.Catalog.Fails = true;
        using (var badAssistantAndNoCatalogue = await host.PostAsync(route, """{"requestId":"request-0001","assistant":{"model":"gpt-test"}}"""))
        {
            await AnswerTesting.AssertErrorAsync(badAssistantAndNoCatalogue, RelayErrors.InvalidAssistant);
        }

        Assert.Equal(1, host.Catalog.Calls);

        // R-6 before R-7: failing catalogue and a selection no catalogue could hold gives
        // ASSISTANT_CATALOG_UNAVAILABLE. (The source's cached copy has aged out; the reader then has a
        // last good catalogue, so this needs a relay that never read one.)
        await using var fresh = await RelayTestHost.StartAsync();
        fresh.Catalog.Fails = true;
        var noCatalogue = fresh.PostAsync(route, """{"requestId":"request-0001","assistant":{"model":"not-a-model","reasoningEffort":"low"}}""");
        await Waiting.UntilAsync(() =>
        {
            fresh.Clock.Advance(ProtocolConstants.CatalogRetryGap);
            return noCatalogue.IsCompleted;
        });
        using var unavailable = await noCatalogue;
        await AnswerTesting.AssertErrorAsync(unavailable, RelayErrors.AssistantCatalogUnavailable);

        // Only the first request of all ran.
        Assert.Single(host.Backend.Runs);
        Assert.Empty(fresh.Backend.Runs);
    }
}
