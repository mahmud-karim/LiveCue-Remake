using System.Net;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// Statement P2-149 (and security test ST-12 for the answer area): a request admitted with a token ends
/// when that pairing is reset, and its run is killed. The request's revocation token is the one the
/// edge got from <c>TryVerify</c> for exactly the pairing that was verified.
/// </summary>
public sealed class AnswerRevocationTests
{
    [Theory]
    [InlineData(AnswerTesting.Assist)]
    [InlineData(AnswerTesting.Summary)]
    public async Task An_assist_in_flight_at_a_reset_is_401_and_its_run_is_killed(string route)
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Backend.NeverReturns = true;
        var oldToken = host.Token;

        var request = host.PostAsync(route, Bodies.Assist("in-flight"));
        await AnswerTesting.RunsStartedAsync(host, 1);
        await host.Relay.ResetPairingAsync(CancellationToken.None);
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            """{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}""",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(1, host.Backend.Cancelled);

        var failed = await host.Events.WaitForAsync<RequestFailed>();
        Assert.Equal(("in-flight", RelayCodes.Unauthorized, "Invalid pairing token."), (failed.RequestId, failed.Code, failed.Message));
        Assert.Equal(0, host.Events.Count<RequestCompleted>());

        // The old token is refused from now on; the new one is served, and the slot is free for it.
        host.Backend.NeverReturns = false;
        using var withOldToken = await host.SendAsync(host.Request(
            HttpMethod.Post, route, TestTokens.Bearer(oldToken), json: Bodies.Assist("old-token")));
        var newToken = await host.ReadTokenAsync();
        using var withNewToken = await host.PostAsync(route, Bodies.Assist("new-token"));

        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(HttpStatusCode.Unauthorized, withOldToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withNewToken.StatusCode);
        Assert.Equal(["in-flight", "new-token"], host.Backend.Runs.Select(run => run.RequestId));
    }

    [Fact]
    public async Task A_reset_while_the_catalogue_is_read_is_401_and_nothing_runs()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Catalog.Gate = AnswerTesting.NewGate();

        var request = host.PostAsync(AnswerTesting.Assist, Bodies.Assist());
        await Waiting.UntilAsync(() => host.Catalog.Calls == 1);
        await host.Relay.ResetPairingAsync(CancellationToken.None);
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));

        await AnswerTesting.AssertErrorAsync(response, RelayErrors.Unauthorized);
        Assert.Empty(host.Backend.Runs);
        await host.Events.SettleAsync();
        Assert.Equal(0, host.Events.Count<RequestStarted>());
        Assert.Equal(0, host.Events.Count<RequestFailed>());
    }
}
