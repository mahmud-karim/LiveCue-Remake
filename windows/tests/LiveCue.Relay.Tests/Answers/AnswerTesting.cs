using System.Text;
using System.Text.Json;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>What the answer tests share: the exact error document, a reply as JSON, gates and waits.</summary>
internal static class AnswerTesting
{
    public const string Assist = "/v1/assist";

    public const string Summary = "/v1/session-summary";

    /// <summary>The response is that error of the table: its status and its exact document.</summary>
    public static async Task AssertErrorAsync(HttpResponseMessage response, RelayError error)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal((error.Status, Encoding.UTF8.GetString(RelayErrors.Body(error).Span)), ((int)response.StatusCode, body));
    }

    /// <summary>The body of a response as a JSON object the caller may keep.</summary>
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return document.RootElement.Clone();
    }

    /// <summary>A gate a fake waits on until the test opens it.</summary>
    public static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Waits, in real time, until the fake backend has started that many runs.</summary>
    public static Task RunsStartedAsync(RelayTestHost host, int runs) =>
        Waiting.UntilAsync(() => host.Backend.Runs.Count >= runs);

    /// <summary>
    /// Sends <c>DELETE /v1/requests/&lt;id&gt;</c> as a correct phone does: the token, protocol 2, no body.
    /// </summary>
    public static Task<HttpResponseMessage> CancelAsync(RelayTestHost host, string requestId) =>
        host.SendAsync(host.Request(HttpMethod.Delete, "/v1/requests/" + requestId));

    /// <summary>
    /// The time, in real seconds, until a new request is no longer refused as busy: the slot is free.
    /// The probe request has no <c>requestId</c>, so it starts nothing.
    /// </summary>
    public static async Task<TimeSpan> UntilTheSlotIsFreeAsync(RelayTestHost host)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            using var probe = await host.PostAsync(Assist, "{}");
            var code = (await ReadJsonAsync(probe)).GetProperty("code").GetString();
            if (code != RelayCodes.AssistantBusy)
            {
                Assert.Equal(RelayCodes.InvalidRequestId, code);
                return DateTime.UtcNow - started;
            }

            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The answer slot was never freed.");
            }

            await Task.Delay(10);
        }
    }
}
