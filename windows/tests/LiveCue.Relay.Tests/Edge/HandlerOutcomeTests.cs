using System.Net;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Step P-11 (W-design 4.4): what the edge answers when a handler does not write a response itself. One
/// test per row of the table, with a handler that does exactly what the row says.
/// </summary>
public sealed class HandlerOutcomeTests
{
    private static readonly byte[] Ok = "{\"ok\":true}"u8.ToArray();

    [Fact]
    public async Task A_relay_exception_is_the_response_of_its_error()
    {
        await using var edge = await EdgeHarness.StartAsync(routes =>
        {
            routes[RouteId.Models] = new ScriptedRoute(_ => throw new RelayException(RelayErrors.AssistantCatalogUnavailable));
            routes[RouteId.Assist] = new ScriptedRoute(_ => throw new RelayException(RelayErrors.AssistantBusy));
        });

        using var models = await edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/models"));

        // Thrown before the body was read: the answer closes the connection (W-design K-4).
        using var raw = await edge.OpenRawAsync();
        await raw.SendAsync(edge.RawHead("POST /v1/assist HTTP/1.1", ("Content-Length", "2")) + "{}");
        var busy = await raw.ReadResponseAsync();
        await edge.Events.SettleAsync();

        await Wire.AssertErrorAsync(models, 503, "ASSISTANT_CATALOG_UNAVAILABLE");
        Assert.Equal(429, busy!.Status);
        Assert.Contains("\"code\":\"ASSISTANT_BUSY\"", busy.Body, StringComparison.Ordinal);
        Assert.Equal("close", busy.Header("Connection"));

        // A coded error is an answer, not a failure: nothing is reported.
        Assert.Empty(edge.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task An_unhandled_exception_is_the_fixed_500_and_a_diagnostic_with_the_type_name_only()
    {
        await using var edge = await EdgeHarness.StartAsync(routes =>
            routes[RouteId.Health] = new ScriptedRoute(_ => throw new InvalidOperationException("PRIVATE DIAGNOSTIC")));

        using var response = await edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/health"));
        var text = await response.Content.ReadAsStringAsync();
        await edge.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(Wire.InternalError, text);
        Assert.Equal(
            new Diagnostic(edge.Clock.GetUtcNow(), "edge", "A request failed unexpectedly. (InvalidOperationException)"),
            Assert.Single(edge.Events.OfType<Diagnostic>()));

        // The listener is untouched by it.
        using var next = await edge.SendAsync(edge.Request(HttpMethod.Post, "/v1/pair/verify", "{}"));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task A_cancellation_that_is_not_the_requests_own_is_a_failure_like_any_other()
    {
        await using var edge = await EdgeHarness.StartAsync(routes =>
            routes[RouteId.Health] = new ScriptedRoute(_ => throw new OperationCanceledException("PRIVATE DIAGNOSTIC")));

        using var response = await edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/health"));
        await edge.Events.SettleAsync();

        await Wire.AssertErrorAsync(response, 500, "INTERNAL_ERROR");
        Assert.Equal(
            "A request failed unexpectedly. (OperationCanceledException)",
            Assert.Single(edge.Events.OfType<Diagnostic>()).Message);
    }

    [Fact]
    public async Task A_handler_that_does_not_answer_is_the_fixed_500()
    {
        await using var edge = await EdgeHarness.StartAsync(routes =>
            routes[RouteId.Health] = new ScriptedRoute(_ => Task.CompletedTask));

        using var response = await edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/health"));
        await edge.Events.SettleAsync();

        // Never the empty 200 the server core would send for a handler that wrote nothing.
        await Wire.AssertErrorAsync(response, 500, "INTERNAL_ERROR");
        Assert.Equal("A route ended without an answer.", Assert.Single(edge.Events.OfType<Diagnostic>()).Message);
    }

    [Fact]
    public async Task A_failure_after_the_response_has_started_aborts_the_connection()
    {
        await using var edge = await EdgeHarness.StartAsync(routes =>
        {
            // A second write on the same request throws; the first one stands.
            routes[RouteId.Health] = new ScriptedRoute(async context =>
            {
                await context.WriteJsonAsync(200, Ok);
                await context.WriteJsonAsync(200, Ok);
            });
            routes[RouteId.Models] = new ScriptedRoute(async context =>
            {
                await context.WriteJsonAsync(200, Ok);
                throw new RelayException(RelayErrors.AssistantFailed);
            });
        });

        foreach (var target in (string[])["/v1/health", "/v1/models"])
        {
            using var raw = await edge.OpenRawAsync();
            var reading = Wire.ReadRawAsync(raw);
            await raw.SendAsync(edge.RawHead($"GET {target} HTTP/1.1"));
            var first = await reading;
            var afterwards = first is null ? null : await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(10));

            // The connection ends. What the handler had written may or may not have left the PC before
            // the abort; what never happens is a second answer on top of it.
            Assert.True(first is null || first.StartsWith("HTTP/1.1 200 OK", StringComparison.Ordinal));
            Assert.Null(afterwards);
            Assert.True(await raw.IsClosedByServerAsync(TimeSpan.FromSeconds(10)));
        }

        await edge.Events.SettleAsync();
        Assert.Equal(
            "A request failed unexpectedly. (InvalidOperationException)",
            Assert.Single(edge.Events.OfType<Diagnostic>()).Message);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(399)]
    [InlineData(101)]
    [InlineData(600)]
    public async Task A_redirect_or_a_status_outside_the_answers_is_never_written(int status)
    {
        await using var edge = await EdgeHarness.StartAsync(routes =>
            routes[RouteId.Health] = new ScriptedRoute(context => context.WriteJsonAsync(status, Ok)));

        using var response = await edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/health"));

        await Wire.AssertErrorAsync(response, 500, "INTERNAL_ERROR");
        Assert.False(response.Headers.Contains("Location"));
    }

    [Fact]
    public async Task A_reset_during_a_request_is_401()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RouteCancellation? cause = null;
        await using var edge = await EdgeHarness.StartAsync(routes => routes[RouteId.Models] = new ScriptedRoute(async context =>
        {
            waiting.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.Work);
            }
            finally
            {
                cause = context.Cancellation;
            }
        }));

        var pending = edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/models"));
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await edge.Services.Pairing.ResetAsync(CancellationToken.None);
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        using var withOldToken = await edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/health"));

        // The request was admitted with a token that is no longer accepted: the same answer as E-3.
        Assert.Equal(RouteCancellation.PairingRevoked, cause);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Wire.Unauthorized, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, withOldToken.StatusCode);

        // That 401 is not one of the three refusals before authentication: the request had passed E-3.
        Assert.Equal(1, edge.Pipeline.TokenNotAccepted);
        Assert.Equal(0, edge.Pipeline.Malformed);
    }

    [Fact]
    public async Task A_stop_during_a_request_aborts_the_connection_without_an_answer()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RouteCancellation? cause = null;
        await using var edge = await EdgeHarness.StartAsync(routes => routes[RouteId.Models] = new ScriptedRoute(async context =>
        {
            waiting.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.Work);
            }
            finally
            {
                cause = context.Cancellation;
            }
        }));

        using var raw = await edge.OpenRawAsync();
        await raw.SendAsync(edge.RawHead("GET /v1/models HTTP/1.1"));
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        edge.Access.BeginStopping();
        var answer = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(10));
        await Wire.UntilAsync(() => edge.Pipeline.Active == 0, "the request ended");
        await edge.Events.SettleAsync();

        Assert.Equal(RouteCancellation.RelayStopping, cause);
        Assert.Null(answer);
        Assert.Equal(0, edge.Pipeline.Gauge.InFlight);
        Assert.Empty(edge.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task A_caller_that_goes_away_ends_the_work_at_once_and_is_not_a_failure()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource<RouteCancellation>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var edge = await EdgeHarness.StartAsync(routes => routes[RouteId.Assist] = new ScriptedRoute(async context =>
        {
            await context.ReadJsonBodyAsync();
            waiting.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.Work);
            }
            finally
            {
                ended.SetResult(context.Cancellation);
            }
        }));

        using (var raw = await edge.OpenRawAsync())
        {
            await raw.SendAsync(edge.RawHead("POST /v1/assist HTTP/1.1", ("Content-Length", "2")) + "{}");
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var cause = await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Wire.UntilAsync(() => edge.Pipeline.Active == 0, "the request ended");
        await edge.Events.SettleAsync();

        Assert.Equal(RouteCancellation.ClientAborted, cause);
        Assert.Equal(0, edge.Pipeline.Gauge.InFlight);
        Assert.Empty(edge.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task A_caller_that_goes_away_in_the_middle_of_its_body_is_not_a_failure_either()
    {
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var edge = await EdgeHarness.StartAsync(routes => routes[RouteId.Assist] = new ScriptedRoute(async context =>
        {
            called.SetResult();
            var body = await context.ReadJsonBodyAsync();
            await context.WriteErrorAsync(body.Error ?? RelayErrors.AssistantFailed);
        }));

        using (var raw = await edge.OpenRawAsync())
        {
            await raw.SendAsync(edge.RawHead("POST /v1/assist HTTP/1.1", ("Content-Length", "1000")) + "{\"half\":");
            await called.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Wire.UntilAsync(() => edge.Clock.ScheduledTimers == 2, "the body is being read");
        }

        await Wire.UntilAsync(() => edge.Pipeline.Active == 0, "the request ended");
        await edge.Events.SettleAsync();

        Assert.Equal(0, edge.Pipeline.Gauge.InFlight);
        Assert.Empty(edge.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task The_handler_sees_the_request_as_the_contract_says()
    {
        RouteContext? seen = null;
        await using var edge = await EdgeHarness.StartAsync(routes => routes[RouteId.CancelRequest] = new ScriptedRoute(context =>
        {
            seen = context;
            return context.WriteJsonAsync(200, Ok);
        }));
        edge.Clock.Advance(TimeSpan.FromSeconds(3));

        using var response = await edge.SendAsync(edge.Request(HttpMethod.Delete, "/v1/requests/A-b_9"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(seen);
        Assert.Equal(RouteId.CancelRequest, seen.Route.Id);
        Assert.Equal("A-b_9", seen.RequestId);
        Assert.Equal(edge.Clock.GetTimestamp(), seen.ArrivalTimestamp);
        Assert.Same(edge.Pipeline, seen.Io);
    }
}
