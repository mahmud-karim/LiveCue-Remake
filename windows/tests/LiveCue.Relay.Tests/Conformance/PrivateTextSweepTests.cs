using System.Collections;
using System.Reflection;
using System.Text.Json;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Speech;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Speech;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>What must never leave the relay, and how the sweeps look for it.</summary>
internal static class PrivateText
{
    /// <summary>The text every failing collaborator of the sweep carries (protocol P2-011).</summary>
    public const string Canary = "PRIVATE DIAGNOSTIC";

    /// <summary>An exception as a collaborator might throw it: the canary, a path and an inner exception.</summary>
    public static Exception Failure(ConformanceRelay? relay = null) => new InvalidOperationException(
        Canary + " at " + (relay?.DataDirectory ?? @"C:\LiveCue-tests\secret") + @"\codex.exe",
        new IOException(Canary + " inner"));

    /// <summary>
    /// Every text an event carries, at any depth: the values of its public properties. (The serialised
    /// form would hide a path behind its escaped backslashes.)
    /// </summary>
    public static string Serialise(RelayEvent relayEvent)
    {
        var texts = new List<string> { relayEvent.GetType().Name };
        Collect(relayEvent, texts, 0);
        return string.Join('\n', texts);
    }

    /// <summary>
    /// Fails when a text holds the canary, a part of a local path, a name of the test machine's user, the
    /// output of the Codex stand-in, or the marks of an exception that was printed.
    /// </summary>
    public static void AssertAbsent(ConformanceRelay relay, IEnumerable<string> texts)
    {
        var forbidden = new List<string>
        {
            "PRIVATE",
            "private diagnostic",
            Path.GetFileName(relay.DataDirectory),
            "LiveCueRemake-tests",
            "LiveCue-tests",
            "codex.exe",
            ".ps1",
            "powershell",
            "TestChild",
            "dotnet",
            "userAgent",
            "codex stand-in",
            "stand-in failure",
            "StackTrace",
            "   at ",
            "Exception:",
            "System.",
            "LiveCue.Relay",
            "Microsoft.",
        };
        if (Environment.UserName.Length >= 4)
        {
            forbidden.Add(Environment.UserName);
        }

        foreach (var text in texts)
        {
            foreach (var word in forbidden)
            {
                Assert.False(
                    text.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"'{word}' left the relay in: {(text.Length <= 400 ? text : text[..400])}");
            }
        }
    }

    private static void Collect(object? value, List<string> texts, int depth)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                texts.Add(text);
                return;
            case IEnumerable items:
                foreach (var item in items)
                {
                    Collect(item, texts, depth + 1);
                }

                return;
        }

        var type = value.GetType();
        // A number, a time or the name of an enumeration member is not text anybody wrote.
        if (type.IsPrimitive || type.IsEnum || depth > 4 || type.Namespace?.StartsWith("LiveCue", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                Collect(property.GetValue(value), texts, depth + 1);
            }
        }
    }
}

/// <summary>A lab runtime every call of which fails with the private text.</summary>
internal sealed class ThrowingLabRuntime : ILabRuntime
{
    public Task<LabHealth?> ProbeAsync(CancellationToken ct) => throw PrivateText.Failure();

    public Task<bool> StartAsync(string model, CancellationToken ct) => throw PrivateText.Failure();

    public Task<bool> StopAsync(CancellationToken ct) => throw PrivateText.Failure();
}

/// <summary>A lab socket connector that fails with the private text instead of the fixed exception.</summary>
internal sealed class ThrowingLabSocketConnector : ILabSocketConnector
{
    public Task<ILabSocket> ConnectAsync(CancellationToken ct) => throw PrivateText.Failure();
}

/// <summary>
/// Protocol P2-011 (test plan 1.7, S-38) across every area: every collaborator fails with the text
/// <c>PRIVATE DIAGNOSTIC</c>, a path and an inner exception; every route and a speech stream are driven;
/// no response body, speech frame, close reason, status object or event contains any of it, and every
/// answer of status 400 or above is an object with the fixed text of a code of protocol 14.1.
/// </summary>
public sealed class PrivateTextSweepTests
{
    [Theory]
    [InlineData("the process runner throws")]
    [InlineData("the catalogue process fails")]
    [InlineData("the answer process fails")]
    [InlineData("the lab runtime throws")]
    [InlineData("the lab socket cannot be opened")]
    [InlineData("the lab says private things")]
    [InlineData("the self-check throws")]
    [InlineData("nothing is installed")]
    public async Task No_response_frame_or_status_contains_private_text(string what)
    {
        await using var relay = await ConformanceRelay.StartAsync(options =>
        {
            switch (what)
            {
                case "the lab runtime throws":
                    options.LabRuntime = new ThrowingLabRuntime();
                    break;
                case "the lab socket cannot be opened":
                    options.LabSocketConnector = new ThrowingLabSocketConnector();
                    break;
                case "nothing is installed":
                    options.CodexFound = false;
                    options.LabConfigured = false;
                    break;
            }
        });

        var seen = new List<string>();
        var streams = 1;
        switch (what)
        {
            case "the process runner throws":
                relay.Codex.Fault = _ => PrivateText.Failure(relay);
                break;
            case "the catalogue process fails":
                relay.Codex.CatalogScenario = "error";
                break;
            case "the lab socket cannot be opened":
                relay.Lab.LoadedModel = "nemotron";
                break;
            case "the lab says private things":
                relay.Lab.LoadedModel = "nemotron";
                relay.LabSockets.AutoRespond = false;
                streams = 4;
                break;
            case "the self-check throws":
                relay.SelfCheck.Handler = (_, _) => throw PrivateText.Failure(relay);
                break;
        }

        // The desktop's own commands, whose results come back as events.
        await relay.Relay.ShowPairingAsync(CancellationToken.None);
        await relay.Events.WaitForAsync<SelfCheckCompleted>();

        // Every HTTP route, with valid input and with each kind of bad input; the answer routes once per
        // way the Codex stand-in can fail.
        foreach (var scenario in what == "the answer process fails" ? (string[])["fail", "no-output", "big"] : ["ok"])
        {
            relay.Codex.ExecScenario = scenario;
            foreach (var route in Wire.HttpRoutes)
            {
                await DriveAsync(relay, relay.RequestFor(route), seen);
                if (route.ReadsBody)
                {
                    await DriveAsync(relay, relay.Request(new HttpMethod(route.Method), route.Target, "[]"), seen);
                    await DriveAsync(relay, relay.Request(new HttpMethod(route.Method), route.Target, """{"model":"../evil","requestId":"a b","assistant":7}"""), seen);
                }

                await DriveAsync(relay, relay.Request(new HttpMethod(route.Method), Wire.TargetOf(route), route.ReadsBody ? "{}" : null, protocol: "1"), seen);
                await DriveAsync(relay, relay.Request(new HttpMethod(route.Method), Wire.TargetOf(route), route.ReadsBody ? "{}" : null, token: TestTokens.WellFormedUnknown), seen);
            }

            await DriveAsync(relay, relay.Request(HttpMethod.Post, "/v1/local-model/start", Bodies.LocalModel("qwen3")), seen);
            await DriveAsync(relay, relay.Request(HttpMethod.Get, "/v1/nowhere"), seen);
        }

        // Speech streams: one per thing the lab can say. A stream that gets as far as "ready" is ended by
        // the phone with a command the relay does not know, so that every stream ends with an error frame.
        for (var stream = 0; stream < streams; stream++)
        {
            await EndToEndRouteTests.UntilSettledAsync(relay);
            if (what == "the lab says private things")
            {
                relay.LabSockets.FirstMessage = stream switch
                {
                    0 => """{"type":"error","message":"PRIVATE DIAGNOSTIC C:\\LiveCue-tests\\lab","detail":"PRIVATE DIAGNOSTIC"}""",
                    1 => """{"type":"ready","model":"qwen3","sampleRate":16000,"note":"PRIVATE DIAGNOSTIC"}""",
                    2 => "PRIVATE DIAGNOSTIC is not JSON",
                    _ => null,
                };
            }

            using var phone = await relay.OpenSpeechAsync("nemotron");
            var frames = new List<string>();
            while (await phone.ReceiveTextAsync() is { } frame)
            {
                frames.Add(frame);
                if (SpeechPhone.TypeOf(SpeechPhone.Parse(frame)) != "ready")
                {
                    continue;
                }

                if (what == "the lab says private things")
                {
                    var lab = relay.LabSockets.Current;
                    lab.Enqueue("""{"type":"transcript","transcript":"Local words","private":"PRIVATE DIAGNOSTIC","path":"C:\\LiveCue-tests\\x"}""");
                    lab.Enqueue("""{"type":"audioProgress","message":"PRIVATE DIAGNOSTIC"}""");
                    lab.Enqueue("""{"type":"PRIVATE DIAGNOSTIC","transcript":"PRIVATE DIAGNOSTIC"}""");
                    lab.Enqueue("""{"type":"speechComplete","transcript":"Local words","turnId":"PRIVATE DIAGNOSTIC"}""");
                    lab.Enqueue("""{"type":"error","message":"PRIVATE DIAGNOSTIC"}""");
                }
                else
                {
                    await phone.SendTextAsync("""{"type":"stop"}""");
                }
            }

            seen.AddRange(frames);
            seen.Add(phone.Socket.CloseStatusDescription ?? string.Empty);
            Assert.NotEqual(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, phone.CloseStatus);
            var error = Assert.Single(frames.Select(SpeechPhone.Parse), frame => SpeechPhone.TypeOf(frame) == "error");
            var code = error.GetProperty("code").GetString()!;
            Assert.Equal(RelayErrors.ForCode(code)?.Text, error.GetProperty("message").GetString());
            Assert.Equal(code, phone.Socket.CloseStatusDescription);
        }

        // The model status after the failures, and what the desktop was told.
        await EndToEndRouteTests.UntilSettledAsync(relay);
        await DriveAsync(relay, relay.Request(HttpMethod.Get, "/v1/local-model"), seen);
        await relay.Relay.ResetPairingAsync(CancellationToken.None);
        await relay.Relay.StopAsync(CancellationToken.None);
        await relay.Events.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        var events = relay.Events.Snapshot();

        PrivateText.AssertAbsent(relay, seen);
        PrivateText.AssertAbsent(relay, events.Select(PrivateText.Serialise));

        // The sweep did reach the failures it set up.
        Assert.Contains(events, item => item is EdgeStatistics);
        switch (what)
        {
            case "the process runner throws":
                Assert.Contains(seen, text => text.Contains("\"code\":\"INTERNAL_ERROR\"", StringComparison.Ordinal));
                Assert.Contains(events.OfType<Diagnostic>(), item => item.Message.EndsWith("(InvalidOperationException)", StringComparison.Ordinal));
                break;
            case "the catalogue process fails":
                Assert.Contains(seen, text => text.Contains("\"code\":\"ASSISTANT_CATALOG_UNAVAILABLE\"", StringComparison.Ordinal));
                break;
            case "the answer process fails":
                Assert.Equal(6, seen.Count(text => text.Contains("\"code\":\"ASSISTANT_FAILED\"", StringComparison.Ordinal)));
                Assert.Equal(6, events.OfType<RequestFailed>().Count());
                break;
            case "the lab runtime throws":
                Assert.Contains(seen, text => text.Contains("\"code\":\"MODEL_LOAD_FAILED\"", StringComparison.Ordinal));
                Assert.Contains(events.OfType<Diagnostic>(), item => item.Message.EndsWith("(InvalidOperationException)", StringComparison.Ordinal));
                break;
            case "the lab socket cannot be opened":
                Assert.Contains(seen, text => text.Contains("\"code\":\"LAB_CONNECTION_FAILED\"", StringComparison.Ordinal));
                break;
            case "the lab says private things":
                Assert.Contains(seen, text => text.Contains("\"code\":\"LAB_INFERENCE_FAILED\"", StringComparison.Ordinal));
                Assert.Contains(seen, text => text.Contains("\"code\":\"MODEL_NOT_READY\"", StringComparison.Ordinal));
                Assert.Contains("""{"type":"transcript","transcript":"Local words"}""", seen);
                Assert.Contains("""{"type":"speechComplete","transcript":"Local words"}""", seen);
                break;
            case "the self-check throws":
                Assert.All(events.OfType<SelfCheckCompleted>(), item => Assert.Equal(SelfCheckResult.NotVerified, item.Result));
                break;
            case "nothing is installed":
                Assert.Contains(seen, text => text.Contains("\"code\":\"LAB_NOT_CONFIGURED\"", StringComparison.Ordinal));
                Assert.Empty(relay.Codex.Requests);
                break;
        }
    }

    /// <summary>
    /// Sends one request and keeps its body. A status of 400 or above must be the object of protocol 5.3:
    /// exactly <c>error</c> and <c>code</c>, a code of 14.1, its status and its fixed sentence.
    /// </summary>
    private static async Task DriveAsync(ConformanceRelay relay, HttpRequestMessage request, List<string> seen)
    {
        var (status, body) = await relay.ExchangeAsync(request);
        seen.Add(body);
        if (status < 400)
        {
            Assert.InRange(status, 200, 299);
            return;
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(["error", "code"], root.EnumerateObject().Select(member => member.Name));
        var error = RelayErrors.ForCode(root.GetProperty("code").GetString()!);
        Assert.NotNull(error);
        Assert.Equal(error.Value.Status, status);
        Assert.Equal(error.Value.Text, root.GetProperty("error").GetString());
    }
}

/// <summary>
/// Test plan S-40: a diagnostic carries a fixed sentence and, at most, the type name of an exception.
/// </summary>
public sealed class DiagnosticTests
{
    [Fact]
    public async Task An_exception_message_never_reaches_a_diagnostic()
    {
        // Every place of the relay that reports an unexpected failure is made to report one, each with an
        // exception whose message, inner exception and stack trace would all be worth reading.
        await using var relay = await ConformanceRelay.StartAsync(options => options.LabRuntime = new ThrowingLabRuntime());
        relay.Codex.Fault = _ => PrivateText.Failure(relay);
        relay.SelfCheck.Handler = (_, _) => throw PrivateText.Failure(relay);

        await relay.Relay.ShowPairingAsync(CancellationToken.None);
        await relay.Events.WaitForAsync<SelfCheckCompleted>();
        foreach (var route in Wire.HttpRoutes)
        {
            using var response = await relay.SendAsync(relay.RequestFor(route));
        }

        using (var phone = await relay.OpenSpeechAsync())
        {
            await phone.ReadToEndAsync();
        }

        await relay.Events.SettleAsync();
        var diagnostics = relay.Events.OfType<Diagnostic>();

        // The edge, the model controller, the speech stream and the pairing self-check each said something.
        Assert.Contains(diagnostics, diagnostic => diagnostic.Source == "edge");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Source == "pairing");
        Assert.True(diagnostics.Select(diagnostic => diagnostic.Source).Distinct().Count() >= 3);
        Assert.True(diagnostics.Count >= 4, $"Only {diagnostics.Count} diagnostics were raised.");
        Assert.All(diagnostics, diagnostic =>
        {
            // "<fixed sentence>." or "<fixed sentence>. (<TypeName>)": letters, spaces and one full stop,
            // then at most one bare type name in brackets.
            Assert.Matches(@"\A[A-Z][A-Za-z ]+\.( \([A-Za-z]+Exception\))?\z", diagnostic.Message);
            Assert.Matches(@"\A[a-z-]+\z", diagnostic.Source);
        });
        PrivateText.AssertAbsent(relay, diagnostics.Select(PrivateText.Serialise));
    }
}

/// <summary>
/// Conversation text is the most private thing the relay handles. It may be in exactly the events that
/// exist to show it to the owner (a request's start and completion, a final speech segment) and in the
/// answer to the phone that sent it. This sweep puts a marker into every text a phone and the lab can
/// send, lets the requests end in every way, and looks for the marker everywhere else.
/// </summary>
public sealed class ConversationTextSweepTests
{
    private const string Spoken = "conversation-canary-4d1f";
    private const string Heard = "lab-canary-93ab";

    [Fact]
    public async Task Conversation_text_is_only_in_the_events_and_answers_that_are_meant_to_carry_it()
    {
        var relay = await ConformanceRelay.StartAsync();
        try
        {
            var answers = new List<string>();
            var body = $$"""{"requestId":"request-0001","assistant":{"model":"gpt-stand-in","reasoningEffort":"low"},"transcript":"{{Spoken}} one","partialTranscript":"{{Spoken}} two","instruction":"{{Spoken}} three","memory":{"summary":"{{Spoken}} four","facts":["{{Spoken}} five"]} }""";

            // Served, failed, too large, without output; then cancelled, and in flight at a reset.
            foreach (var scenario in (string[])["ok", "fail", "big", "no-output"])
            {
                relay.Codex.ExecScenario = scenario;
                answers.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", body)).Body);
                answers.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/session-summary", body)).Body);
            }

            relay.Codex.ExecScenario = "hang";
            var run = relay.Codex.ExecReportFiles.Count;
            var cancelled = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", body);
            await relay.Codex.ReadExecReportAsync(run);
            answers.Add((await relay.ExchangeAsync(HttpMethod.Delete, "/v1/requests/request-0001")).Body);
            answers.Add((await cancelled).Body);
            var revoked = relay.ExchangeAsync(HttpMethod.Post, "/v1/session-summary", body);
            await relay.Codex.ReadExecReportAsync(run + 1);
            await relay.Relay.ResetPairingAsync(CancellationToken.None);
            answers.Add((await revoked).Body);
            await relay.ReadTokenAsync();

            // Refused before a run: the text is in the body, and the body is never looked at or kept.
            relay.Codex.ExecScenario = "ok";
            answers.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", body.Replace("gpt-stand-in", "gpt-other", StringComparison.Ordinal))).Body);
            answers.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", body.Replace("request-0001", "a b", StringComparison.Ordinal))).Body);
            answers.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/pair/verify", body)).Body);
            answers.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/local-model/start", body)).Body);
            answers.Add((await relay.ExchangeAsync(relay.Request(HttpMethod.Post, "/v1/assist", body, token: TestTokens.WellFormedUnknown))).Body);
            await relay.Relay.SetAcceptingAsync(false);
            answers.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", body)).Body);
            await relay.Relay.SetAcceptingAsync(true);

            // A stream: partial text, an unknown event and a lab error that all hold the lab's marker; one
            // final segment.
            relay.LabSockets.AutoRespond = false;
            var frames = new List<string>();
            using (var phone = await relay.OpenReadyStreamAsync())
            {
                var lab = relay.LabSockets.Current;
                lab.Enqueue($$"""{"type":"transcript","transcript":"{{Heard}} partial","debug":"{{Heard}} debug"}""");
                lab.Enqueue($$"""{"type":"audioProgress","text":"{{Heard}} progress"}""");
                lab.Enqueue($$"""{"type":"speechComplete","turnId":1,"transcript":"{{Heard}} final"}""");
                lab.Enqueue($$"""{"type":"error","message":"{{Heard}} error"}""");
                frames.AddRange((await phone.ReadToEndAsync()).Frames);
            }

            await relay.Relay.StopAsync(CancellationToken.None);
            await relay.Events.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            var events = relay.Events.Snapshot();

            // What the phone said is in the start event of each request that began, and nowhere else: not
            // in a failure, a notice, a diagnostic or a model or stream status, and not in any answer.
            Assert.Equal(10, events.OfType<RequestStarted>().Count());
            Assert.All(events.OfType<RequestStarted>(), started =>
            {
                Assert.Equal(Spoken + " one", started.Transcript);
                Assert.Contains(Spoken + " five", started.PayloadJson, StringComparison.Ordinal);
            });
            foreach (var other in events.Where(item => item is not RequestStarted))
            {
                Assert.False(
                    PrivateText.Serialise(other).Contains(Spoken, StringComparison.Ordinal),
                    $"A {other.GetType().Name} event carries what the phone said.");
            }

            Assert.All(answers, answer => Assert.DoesNotContain(Spoken, answer, StringComparison.Ordinal));
            Assert.Equal(2, events.OfType<RequestCompleted>().Count());
            Assert.Equal(8, events.OfType<RequestFailed>().Count());

            // What the lab heard: the phone gets the partial and the final text, and nothing of the debug
            // member, the unknown event or the error text. The desktop gets the final segment only.
            Assert.Equal(
                [
                    $$"""{"type":"transcript","transcript":"{{Heard}} partial"}""",
                    $$"""{"type":"speechComplete","turnId":1,"transcript":"{{Heard}} final"}""",
                    """{"type":"error","code":"LAB_INFERENCE_FAILED","message":"PC speech inference failed."}""",
                ],
                frames.Where(frame => !frame.Contains("\"loading\"", StringComparison.Ordinal) && !frame.Contains("\"ready\"", StringComparison.Ordinal)));
            Assert.Equal(Heard + " final", Assert.Single(events.OfType<SpeechText>()).Text);
            foreach (var other in events.Where(item => item is not SpeechText))
            {
                Assert.False(
                    PrivateText.Serialise(other).Contains(Heard, StringComparison.Ordinal),
                    $"A {other.GetType().Name} event carries what the lab heard.");
            }

            // And nothing of either is left in a file.
            foreach (var file in relay.DataFiles())
            {
                var text = await File.ReadAllTextAsync(Path.Combine(relay.DataDirectory, file));
                Assert.DoesNotContain(Spoken, text, StringComparison.Ordinal);
                Assert.DoesNotContain(Heard, text, StringComparison.Ordinal);
            }
        }
        finally
        {
            await relay.DisposeAsync();
        }
    }
}
