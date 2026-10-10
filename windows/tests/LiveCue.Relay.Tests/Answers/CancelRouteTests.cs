using System.Net;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// Route 9, <c>DELETE /v1/requests/{id}</c> (protocol 6.8; statement P2-058), through a real loopback
/// listener. Cancelling is truthful: "cancelled" is answered only for a request that then ends as
/// 409 REQUEST_CANCELLED, and "not found" only when no request with exactly that id is in flight.
/// </summary>
public sealed class CancelRouteTests
{
    [Theory]
    [InlineData(AnswerTesting.Assist, AnswerKind.Assist)]
    [InlineData(AnswerTesting.Summary, AnswerKind.Summary)]
    public async Task Cancel_kills_the_run_and_the_request_ends_409(string route, AnswerKind kind)
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Backend.NeverReturns = true;
        const string Id = "6F9619FF-8B86-4011-B42D-00C04FC964FF";

        var request = host.PostAsync(route, Bodies.Assist(Id));
        await AnswerTesting.RunsStartedAsync(host, 1);
        using var cancel = await AnswerTesting.CancelAsync(host, Id);
        using var cancelled = await request;

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("""{"ok":true,"cancelled":true}""", await cancel.Content.ReadAsStringAsync());

        // The run was killed, and the request says "cancelled": not a failure, not a time-out.
        Assert.Equal(1, host.Backend.Cancelled);
        Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
        Assert.Equal(
            """{"error":"The request was cancelled.","code":"REQUEST_CANCELLED"}""",
            await cancelled.Content.ReadAsStringAsync());

        // The desktop is told the same, once.
        var failed = await host.Events.WaitForAsync<RequestFailed>();
        await host.Events.SettleAsync();
        Assert.Equal((Id, kind, RelayCodes.RequestCancelled), (failed.RequestId, failed.Kind, failed.Code));
        Assert.Equal("The request was cancelled.", failed.Message);
        Assert.Single(host.Events.OfType<RequestFailed>());
        Assert.Equal(0, host.Events.Count<RequestCompleted>());

        // The request has ended: a second cancel finds nothing, and the id can be used again.
        using var again = await AnswerTesting.CancelAsync(host, Id);
        await AnswerTesting.AssertErrorAsync(again, RelayErrors.RequestNotFound);
        host.Backend.NeverReturns = false;
        using var reused = await host.PostAsync(route, Bodies.Assist(Id));
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
    }

    [Fact]
    public async Task An_unknown_id_is_404_request_not_found()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var nothingInFlight = await AnswerTesting.CancelAsync(host, "request-0001");

        Assert.Equal(HttpStatusCode.NotFound, nothingInFlight.StatusCode);
        Assert.Equal(
            """{"error":"No request with that id is in progress.","code":"REQUEST_NOT_FOUND"}""",
            await nothingInFlight.Content.ReadAsStringAsync());

        // With a request in flight, another id cancels nothing. Ids are compared exactly: a different
        // letter case, a prefix and a longer text are other ids.
        var gate = host.Backend.Gate = AnswerTesting.NewGate();
        var request = host.PostAsync(AnswerTesting.Assist, Bodies.Assist("Request-ABC"));
        await AnswerTesting.RunsStartedAsync(host, 1);
        foreach (var other in (string[])["request-abc", "REQUEST-ABC", "Request-AB", "Request-ABCD", "another", "Request_ABC"])
        {
            using var response = await AnswerTesting.CancelAsync(host, other);
            await AnswerTesting.AssertErrorAsync(response, RelayErrors.RequestNotFound);
        }

        Assert.Equal(0, host.Backend.Cancelled);
        gate.SetResult();
        using var served = await request;
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        await host.Events.WaitForAsync<RequestCompleted>();
        Assert.Equal(0, host.Events.Count<RequestFailed>());
    }

    [Theory]
    [InlineData("DELETE /v1/requests/a%2Fb HTTP/1.1")]
    [InlineData("DELETE /v1/requests/a/b HTTP/1.1")]
    [InlineData("DELETE /v1/requests/ HTTP/1.1")]
    [InlineData("DELETE /v1/requests/a?b=c HTTP/1.1")]
    [InlineData("DELETE /v1/requests/a%0A HTTP/1.1")]
    [InlineData("DELETE /v1/requests/<81> HTTP/1.1")]
    public async Task An_encoded_slash_is_404_not_found(string requestLine)
    {
        await using var host = await RelayTestHost.StartAsync();
        using var raw = await host.OpenRawAsync();

        // "<81>" stands for an id one character longer than the shape allows.
        requestLine = requestLine.Replace("<81>", new string('a', ProtocolConstants.RequestIdMaxLength + 1), StringComparison.Ordinal);
        await raw.SendAsync(host.RawHead(requestLine));
        var response = await raw.ReadResponseAsync();

        // The edge answers before the route: never the route's own 404, and never a 500.
        Assert.NotNull(response);
        Assert.Equal(404, response.Status);
        Assert.Equal("""{"error":"Endpoint not found.","code":"NOT_FOUND"}""", response.Body);
        await host.Events.SettleAsync();
        Assert.Equal(0, host.Events.Count<Diagnostic>());
    }

    [Fact]
    public async Task A_cancel_while_the_catalogue_is_read_is_truthful_too()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Catalog.Gate = AnswerTesting.NewGate();

        // The request has passed R-4 and R-5 and waits at R-6: it is in flight under its id.
        var request = host.PostAsync(AnswerTesting.Assist, Bodies.Assist("early-cancel"));
        await Waiting.UntilAsync(() => host.Catalog.Calls == 1);
        using var cancel = await AnswerTesting.CancelAsync(host, "early-cancel");
        using var cancelled = await request;

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        await AnswerTesting.AssertErrorAsync(cancelled, RelayErrors.RequestCancelled);

        // Nothing ran, and the desktop was told about no request: none had started.
        Assert.Empty(host.Backend.Runs);
        await host.Events.SettleAsync();
        Assert.Equal(0, host.Events.Count<RequestStarted>());
        Assert.Equal(0, host.Events.Count<RequestFailed>());
        await AnswerTesting.UntilTheSlotIsFreeAsync(host);
    }

    [Fact]
    public async Task A_cancel_storm_cancels_once_and_the_slot_always_frees()
    {
        await using var host = await RelayTestHost.StartAsync();

        for (var round = 0; round < 5; round++)
        {
            host.Backend.NeverReturns = true;
            var id = "storm-" + round;
            var request = host.PostAsync(AnswerTesting.Assist, Bodies.Assist(id));
            await AnswerTesting.RunsStartedAsync(host, (2 * round) + 1);

            var cancels = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AnswerTesting.CancelAsync(host, id)));
            using var cancelled = await request;

            // At least one cancel was in time; every one of them told the truth.
            Assert.Contains(cancels, cancel => cancel.StatusCode == HttpStatusCode.OK);
            Assert.All(cancels, cancel => Assert.Contains(cancel.StatusCode, (HttpStatusCode[])[HttpStatusCode.OK, HttpStatusCode.NotFound]));
            await AnswerTesting.AssertErrorAsync(cancelled, RelayErrors.RequestCancelled);
            foreach (var cancel in cancels)
            {
                cancel.Dispose();
            }

            host.Backend.NeverReturns = false;
            using var next = await host.PostAsync(AnswerTesting.Summary, Bodies.Summary("after-" + round));
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        }

        Assert.Equal(5, host.Backend.Cancelled);
    }
}
