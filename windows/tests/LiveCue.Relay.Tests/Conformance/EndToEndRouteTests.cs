using System.Net.WebSockets;
using System.Text.Json;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Processes;
using LiveCue.Relay.Tests.Speech;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>
/// Every row of protocol 6.1, once, with valid input, through one relay in which every area is the
/// production one (test plan 1.7). What an area test proves with a fake neighbour is proved here with the
/// real neighbour: the catalogue the answer route validates against is the one the stand-in Codex process
/// returned, the answer is the file that process wrote, and the stream holds the lease the stop command
/// looks at.
/// </summary>
public sealed class EndToEndRouteTests
{
    [Fact]
    public async Task Each_route_once_with_valid_input()
    {
        await using var relay = await ConformanceRelay.StartAsync();

        // Route 1.
        var (status, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/health");
        Assert.Equal(200, status);
        Assert.Equal("""{"ok":true,"protocol":2,"relayVersion":"1.0.0"}""", body);

        // Route 2.
        (status, body) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/pair/verify", "{}");
        Assert.Equal(200, status);
        Assert.Equal("""{"ok":true}""", body);

        // Route 3: what the stand-in app-server offered, filtered (the hidden entry and the one without a
        // model are gone; the entries received before the cursor are the catalogue).
        (status, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/models");
        Assert.Equal(200, status);
        Assert.Equal("""{"models":[{"id":"gpt-stand-in","name":"Stand-in","reasoningEfforts":["low","high"]}]}""", body);

        // Route 4: all five members, stopped.
        (status, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
        Assert.Equal(200, status);
        using (var stopped = JsonDocument.Parse(body))
        {
            Assert.Equal(
                ["state", "model", "busy", "elapsedSeconds", "message"],
                stopped.RootElement.EnumerateObject().Select(member => member.Name));
            Assert.Equal("stopped", stopped.RootElement.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, stopped.RootElement.GetProperty("model").ValueKind);
        }

        // Route 5: 202 with "starting", and then route 4 reports "ready".
        (status, body) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("nemotron"));
        Assert.Equal(202, status);
        Assert.Equal("starting", StateOf(body));
        await UntilStateAsync(relay, "ready");
        Assert.Equal(1, relay.Lab.StartCalls);

        // Route 10: a whole stream over the model that route 5 loaded; no second start.
        using (var phone = await relay.OpenSpeechAsync("nemotron"))
        {
            Assert.Equal("nemotron", (await phone.ExpectAsync("loading")).GetProperty("provider").GetString());
            var ready = await phone.WaitForAsync("ready");
            Assert.Equal("nemotron", ready.GetProperty("provider").GetString());
            Assert.Equal(16000, ready.GetProperty("sampleRate").GetInt32());
            Assert.Equal(JsonValueKind.Number, ready.GetProperty("handshakeMs").ValueKind);

            await phone.SendAudioAsync(2560);
            Assert.Equal("""{"type":"transcript","transcript":"Local words"}""", await phone.ReceiveTextAsync());
            await phone.SendEndStreamAsync();
            var ending = await phone.ReadToEndAsync();
            Assert.Equal(["speechComplete", "streamComplete"], ending.Types);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
            Assert.Equal(2560, relay.LabSockets.Current.AudioBytes);
            Assert.Equal(1, relay.Lab.StartCalls);
        }

        // Route 6: 202 with "stopping", and then "stopped".
        await Wire.UntilAsync(() => relay.LabSockets.Current.Disposed, "the stream let go of the lab socket");
        (status, body) = await SendUntilNotBusyAsync(relay, "/v1/local-model/stop", Bodies.LocalModel("nemotron"));
        Assert.Equal(202, status);
        Assert.Equal("stopping", StateOf(body));
        await UntilStateAsync(relay, "stopped");
        Assert.Equal(1, relay.Lab.StopCalls);

        // Route 7: the answer is the file the Codex stand-in wrote, checked and rewritten by the relay.
        (status, body) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody());
        Assert.Equal(200, status);
        using (var assist = JsonDocument.Parse(body))
        {
            var root = assist.RootElement;
            Assert.Equal(
                ["detectedQuestion", "answer", "details", "memory", "execution"],
                root.EnumerateObject().Select(member => member.Name));
            Assert.Equal("Stand-in answer.", root.GetProperty("answer").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("memory").GetProperty("throughSegmentId").ValueKind);
            Assert.Equal(ConformanceRelay.Model, root.GetProperty("execution").GetProperty("model").GetString());
            Assert.Equal(ConformanceRelay.Effort, root.GetProperty("execution").GetProperty("reasoningEffort").GetString());
        }

        // Route 8.
        (status, body) = await relay.ExchangeAsync(
            HttpMethod.Post, "/v1/session-summary", ConformanceRelay.AnswerBody("request-0002"));
        Assert.Equal(200, status);
        using (var summary = JsonDocument.Parse(body))
        {
            Assert.Equal(
                ["title", "summary", "keyPoints", "actionItems", "execution"],
                summary.RootElement.EnumerateObject().Select(member => member.Name));
            Assert.Equal("Stand-in title", summary.RootElement.GetProperty("title").GetString());
        }

        // The catalogue process ran once for the three routes that needed the catalogue.
        Assert.Single(relay.Codex.CatalogRequests);

        // Route 9: cancel kills the real process, and the cancelled request ends 409.
        relay.Codex.ExecScenario = "hang";
        var hanging = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-0003"));
        var report = await relay.Codex.ReadExecReportAsync(2);
        (status, body) = await relay.ExchangeAsync(HttpMethod.Delete, "/v1/requests/request-0003");
        Assert.Equal(200, status);
        Assert.Equal("""{"ok":true,"cancelled":true}""", body);
        (status, body) = await hanging.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(409, status);
        Assert.Contains("\"code\":\"REQUEST_CANCELLED\"", body, StringComparison.Ordinal);
        await TestChild.WaitUntilGoneAsync(report.ProcessId);
        Assert.False(Directory.Exists(report.WorkingFolder));

        // And an id nobody holds.
        (status, body) = await relay.ExchangeAsync(HttpMethod.Delete, "/v1/requests/request-0003");
        Assert.Equal(404, status);
        Assert.Contains("\"code\":\"REQUEST_NOT_FOUND\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_answer_of_a_full_relay_has_only_the_allowed_headers()
    {
        // Protocol P2-008 over the real areas: successes and refusals of every route.
        await using var relay = await ConformanceRelay.StartAsync();
        var requests = new List<string>();
        foreach (var route in Wire.HttpRoutes)
        {
            var line = $"{route.Method} {Wire.TargetOf(route)} HTTP/1.1";
            requests.Add(route.ReadsBody ? relay.RawPost(Wire.TargetOf(route), "{}") : relay.RawHead(line));
            requests.Add(route.ReadsBody ? relay.RawPost(Wire.TargetOf(route), "[") : relay.RawHead(line, ("Cookie", "a=b")));
        }

        requests.Add(relay.RawPost("/v1/assist", ConformanceRelay.AnswerBody()));
        requests.Add(relay.RawPost("/v1/local-model/start", Bodies.LocalModel("qwen3")));

        foreach (var request in requests)
        {
            var response = await Wire.ExchangeAsync(relay.Port, request);
            Assert.NotNull(response);
            var names = Wire.HeaderNames(response);
            Assert.All(names, name => Assert.Contains(name, Wire.AllowedResponseHeaders));
            Assert.Contains("Content-Type: application/json; charset=utf-8", Wire.HeaderLines(response));
            Assert.Contains("Cache-Control: no-store", Wire.HeaderLines(response));
            Assert.Contains("X-Content-Type-Options: nosniff", Wire.HeaderLines(response));
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotMatch("^HTTP/1.1 3", Wire.StatusLine(response));
        }
    }

    internal static string? StateOf(string statusJson)
    {
        using var document = JsonDocument.Parse(statusJson);
        return document.RootElement.GetProperty("state").GetString();
    }

    internal static async Task UntilStateAsync(ConformanceRelay relay, string state)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            var (status, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
            Assert.Equal(200, status);
            if (StateOf(body) == state)
            {
                return;
            }

            Assert.True(DateTime.UtcNow < until, $"Route 4 never reported '{state}'; last: {body}");
            await Task.Delay(10);
        }
    }

    /// <summary>Waits until no model operation runs: the status is neither "starting" nor "stopping".</summary>
    internal static async Task UntilSettledAsync(ConformanceRelay relay)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            var (_, body) = await relay.ExchangeAsync(HttpMethod.Get, "/v1/local-model");
            if (StateOf(body) is not ("starting" or "stopping"))
            {
                return;
            }

            Assert.True(DateTime.UtcNow < until, "A model operation never ended: " + body);
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// A model command right after a stream ended: the lease is given back a moment after the phone has
    /// seen its close frame, so MODEL_BUSY may be answered once more.
    /// </summary>
    internal static async Task<(int Status, string Body)> SendUntilNotBusyAsync(ConformanceRelay relay, string target, string json)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            var answer = await relay.ExchangeAsync(HttpMethod.Post, target, json);
            if (answer.Status != 409 || DateTime.UtcNow > until)
            {
                return answer;
            }

            await Task.Delay(10);
        }
    }
}
