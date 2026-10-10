using System.Text.Json;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-011 for the errors the edge itself writes (the sweep over every area is in the conformance
/// tests): each is a JSON object with exactly a string <c>error</c> and a string <c>code</c> listed in
/// protocol 14.1, and none carries internal detail.
/// </summary>
public sealed class ErrorBodyTests
{
    private const string PrivateText = "PRIVATE DIAGNOSTIC";

    [Fact]
    public async Task Every_error_is_an_object_with_error_and_a_listed_code()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var edge = await EdgeHarness.StartAsync(routes =>
        {
            // Collaborators that fail with a private text, each in another way.
            routes[RouteId.Health] = new ScriptedRoute(_ => throw new InvalidOperationException(PrivateText));
            routes[RouteId.LocalModelStatus] = new ScriptedRoute(_ => throw new IOException(@"D:\made-up-folder\" + PrivateText));
            routes[RouteId.Models] = new ScriptedRoute(async context =>
            {
                await hold.Task.WaitAsync(context.Work);
                throw new RelayException(RelayErrors.AssistantCatalogUnavailable);
            });
            routes[RouteId.CancelRequest] = new ScriptedRoute(context => Task.Delay(Timeout.InfiniteTimeSpan, context.Work));
        });

        var started = edge.Clock.GetUtcNow();
        var answers = new List<(int Status, string Code, string Body)>();
        async Task CollectAsync(HttpRequestMessage request, int status, string code)
        {
            using var response = await edge.SendAsync(request);
            answers.Add((status, code, await response.Content.ReadAsStringAsync()));
            Assert.Equal(status, (int)response.StatusCode);
        }

        // E-1 to E-3.
        var noToken = edge.Request(HttpMethod.Get, "/v1/health");
        noToken.Headers.Remove("Authorization");
        await CollectAsync(noToken, 401, "UNAUTHORIZED");

        // E-4, E-5, E-6.
        await CollectAsync(edge.Request(HttpMethod.Get, "/v1/unknown"), 404, "NOT_FOUND");
        await CollectAsync(edge.Request(HttpMethod.Get, "/v1/health", protocol: "3"), 400, "PROTOCOL_MISMATCH");
        await CollectAsync(edge.Request(HttpMethod.Get, "/v1/health", json: "{}"), 400, "INVALID_REQUEST");

        // Protocol 5.4.
        await CollectAsync(edge.Request(HttpMethod.Post, "/v1/pair/verify", json: "nope"), 400, "INVALID_JSON");
        using (var raw = await edge.OpenRawAsync())
        {
            // By hand, with the read already waiting: the server core closes this connection at once.
            var reading = Wire.ReadRawAsync(raw);
            await raw.SendAsync(edge.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "131073")));
            var tooLarge = await reading;
            Assert.Equal("HTTP/1.1 413 Payload Too Large", Wire.StatusLine(tooLarge!));
            answers.Add((413, "PAYLOAD_TOO_LARGE", Wire.BodyOf(tooLarge!)));
        }

        // The last resort: an exception nobody expected, with a private text and a path in it.
        await CollectAsync(edge.Request(HttpMethod.Get, "/v1/health"), 500, "INTERNAL_ERROR");
        await CollectAsync(edge.Request(HttpMethod.Get, "/v1/local-model"), 500, "INTERNAL_ERROR");

        // E-7, then a handler's own coded error, then the deadline.
        var held = Enumerable.Range(0, ProtocolConstants.MaxRequestsInFlight)
            .Select(_ => edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/models")))
            .ToList();
        await Wire.UntilAsync(() => edge.Pipeline.Gauge.InFlight == ProtocolConstants.MaxRequestsInFlight, "the gauge is full");
        await CollectAsync(edge.Request(HttpMethod.Get, "/v1/health"), 429, "TOO_MANY_REQUESTS");
        hold.SetResult();
        foreach (var response in await Task.WhenAll(held))
        {
            answers.Add((503, "ASSISTANT_CATALOG_UNAVAILABLE", await response.Content.ReadAsStringAsync()));
            Assert.Equal(503, (int)response.StatusCode);
            response.Dispose();
        }

        var late = edge.SendAsync(edge.Request(HttpMethod.Delete, "/v1/requests/abc"));
        await Wire.UntilAsync(() => edge.Pipeline.Gauge.InFlight == 1, "the request is held");
        edge.Clock.Advance(ProtocolConstants.CancelDeadline);
        using (var response = await late)
        {
            answers.Add((504, "DEADLINE_EXCEEDED", await response.Content.ReadAsStringAsync()));
        }

        await edge.Events.SettleAsync();

        foreach (var (status, code, body) in answers)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var listed = RelayErrors.ForCode(code);

            // An object with exactly the two members, both strings, and a code of the table with its text.
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.Equal(["error", "code"], root.EnumerateObject().Select(member => member.Name));
            Assert.Equal(JsonValueKind.String, root.GetProperty("error").ValueKind);
            Assert.Equal(code, root.GetProperty("code").GetString());
            Assert.NotNull(listed);
            Assert.Equal(status, listed.Value.Status);
            Assert.Equal(listed.Value.Text, root.GetProperty("error").GetString());

            // No exception text, no path.
            Assert.DoesNotContain("PRIVATE", body, StringComparison.Ordinal);
            Assert.DoesNotContain("made-up-folder", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        }

        // The desktop learns the type of the two unexpected failures, and nothing of their text.
        var diagnostics = edge.Events.OfType<Diagnostic>();
        Assert.Equal(
            [
                new Diagnostic(started, "edge", "A request failed unexpectedly. (InvalidOperationException)"),
                new Diagnostic(started, "edge", "A request failed unexpectedly. (IOException)"),
            ],
            diagnostics);
        Assert.All(edge.Events.Snapshot(), relayEvent =>
            Assert.DoesNotContain("PRIVATE", relayEvent.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Every_edge_error_is_in_the_table_with_its_status()
    {
        (RelayError Error, int Status)[] expected =
        [
            (RelayErrors.Unauthorized, 401),
            (RelayErrors.NotFound, 404),
            (RelayErrors.ProtocolMismatch, 400),
            (RelayErrors.InvalidRequest, 400),
            (RelayErrors.InvalidJson, 400),
            (RelayErrors.PayloadTooLarge, 413),
            (RelayErrors.RequestTimeout, 408),
            (RelayErrors.TooManyRequests, 429),
            (RelayErrors.DeadlineExceeded, 504),
            (RelayErrors.AssistantTimeout, 504),
            (RelayErrors.InternalError, 500),
        ];

        Assert.All(expected, row =>
        {
            Assert.Equal(row.Status, row.Error.Status);
            Assert.Contains(row.Error, RelayErrors.All);
        });
    }
}
