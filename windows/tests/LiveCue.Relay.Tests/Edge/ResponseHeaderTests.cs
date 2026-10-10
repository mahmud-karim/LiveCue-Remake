using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-008 (protocol 5.3): what every response written by relay code carries, and what it never
/// carries. The allowlist is the one of W-design K-1: the three fixed headers, the length, and what the
/// server core itself adds (<c>Date</c>, and <c>Connection</c> when the connection ends).
/// </summary>
public sealed class ResponseHeaderTests
{
    [Fact]
    public async Task Every_response_has_only_the_allowed_headers()
    {
        await using var edge = await EdgeHarness.StartAsync(routes =>
        {
            routes[RouteId.LocalModelStatus] = new ScriptedRoute(_ => throw new InvalidOperationException("failure"));
            routes[RouteId.Models] = new ScriptedRoute(context => Task.Delay(Timeout.InfiniteTimeSpan, context.Work));
            routes[RouteId.CancelRequest] = new ScriptedRoute(_ => throw new RelayException(RelayErrors.RequestNotFound));
        });
        var bearer = ("Authorization", TestTokens.Bearer(edge.Token));

        // One request per kind of answer: status, then the head and body that cause it.
        var cases = new List<(int Status, string Request)>
        {
            (200, edge.RawHead("GET /v1/health HTTP/1.1")),
            (200, edge.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "2")) + "{}"),
            (401, Wire.Head(edge.Port, "GET /v1/health HTTP/1.1")),
            (401, Wire.Head(edge.Port, "POST /v1/unknown HTTP/1.1", ("Origin", "https://bad.test"), bearer)),
            (404, edge.RawHead("GET /v1/unknown HTTP/1.1")),
            (404, edge.RawHead("DELETE /v1/requests/abc HTTP/1.1")),
            (400, Wire.Head(edge.Port, "GET /v1/health HTTP/1.1", bearer)),
            (400, edge.RawHead("GET /v1/health HTTP/1.1", ("Content-Length", "2")) + "{}"),
            (400, edge.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "2")) + "[]"),
            (413, edge.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "131073"))),
            (500, edge.RawHead("GET /v1/local-model HTTP/1.1")),
        };

        foreach (var (status, request) in cases)
        {
            var response = await ExchangeAsync(edge, request);
            AssertHeaders(response, status);

            // A cookie, forwarding headers and headers nobody knows change nothing: not one byte.
            var insert = request.IndexOf("\r\n", StringComparison.Ordinal) + 2;
            var withExtras = request.Insert(
                insert,
                "Cookie: session=abc; token=" + edge.Token + "\r\n"
                + "X-Forwarded-For: 203.0.113.9\r\n"
                + "Forwarded: for=203.0.113.9;proto=https\r\n"
                + "X-Real-IP: 203.0.113.9\r\n"
                + "X-Unknown: ignored\r\n"
                + "Accept: text/html\r\n"
                + "User-Agent: test\r\n");
            Assert.Equal(response, await ExchangeAsync(edge, withExtras));
        }

        // The answers that need the clock or a full gauge: 504 and 429.
        var held = Enumerable.Range(0, ProtocolConstants.MaxRequestsInFlight)
            .Select(_ => edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/models")))
            .ToList();
        await Wire.UntilAsync(() => edge.Pipeline.Gauge.InFlight == ProtocolConstants.MaxRequestsInFlight, "the gauge is full");
        AssertHeaders(await ExchangeAsync(edge, edge.RawHead("GET /v1/health HTTP/1.1")), 429);

        using var late = await edge.OpenRawAsync();
        edge.Clock.Advance(ProtocolConstants.ModelsDeadline);
        foreach (var response in await Task.WhenAll(held))
        {
            response.Dispose();
        }

        await Wire.UntilAsync(() => edge.Pipeline.Gauge.InFlight == 0, "the held requests ended");
        await late.SendAsync(edge.RawHead("GET /v1/models HTTP/1.1"));
        await Wire.UntilAsync(() => edge.Pipeline.Gauge.InFlight == 1, "the request is held");
        edge.Clock.Advance(ProtocolConstants.ModelsDeadline);
        AssertHeaders(Wire.WithoutDate((await Wire.ReadRawAsync(late))!), 504);
    }

    [Fact]
    public async Task The_upgrade_answer_has_no_subprotocol_no_extension_and_nothing_of_a_browser()
    {
        await using var host = await RelayTestHost.StartAsync();
        using var raw = await host.OpenRawAsync();

        // The client offers a subprotocol and compression; the relay accepts neither.
        await raw.SendAsync(host.RawHead(
            "GET /v1/speech HTTP/1.1",
            [
                .. Wire.UpgradeHeaders,
                ("X-LiveCue-Speech-Model", "nemotron"),
                ("Sec-WebSocket-Protocol", "chat, superchat"),
                ("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits"),
            ]));
        var response = await Wire.ReadRawAsync(raw, hasBody: false);

        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 101 Switching Protocols", Wire.StatusLine(response));
        Assert.Equal(
            ["Connection", "Date", "Sec-WebSocket-Accept", "Upgrade"],
            Wire.HeaderNames(response).Order(StringComparer.Ordinal));
    }

    private static async Task<string> ExchangeAsync(EdgeHarness edge, string request)
    {
        using var raw = await edge.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw);
        await raw.SendAsync(request);
        var response = await reading;
        Assert.NotNull(response);
        return Wire.WithoutDate(response);
    }

    private static void AssertHeaders(string response, int status)
    {
        Assert.StartsWith($"HTTP/1.1 {status} ", Wire.StatusLine(response), StringComparison.Ordinal);
        var lines = Wire.HeaderLines(response);
        var names = Wire.HeaderNames(response);

        // The three fixed headers, exactly once each, and the length of the body.
        Assert.Contains("Content-Type: application/json; charset=utf-8", lines);
        Assert.Contains("Cache-Control: no-store", lines);
        Assert.Contains("X-Content-Type-Options: nosniff", lines);
        Assert.Contains($"Content-Length: {Wire.BodyOf(response).Length}", lines);
        Assert.Contains("Date: -", lines);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Nothing else: no Server, no cookie, no CORS header, no Location.
        Assert.All(names, name => Assert.Contains(name, Wire.AllowedResponseHeaders));
        Assert.DoesNotContain(names, name => name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Server", names);
        Assert.DoesNotContain("Set-Cookie", names);
        Assert.DoesNotContain("Location", names);
        if (names.Contains("Connection"))
        {
            Assert.Contains("Connection: close", lines);
        }
    }
}
