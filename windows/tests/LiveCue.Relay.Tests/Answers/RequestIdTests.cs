using System.Net;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// Step R-4: <c>requestId</c> is a string of the shape C-03, checked over the whole text (statement
/// P2-057; security test ST-13). The same shape, through the same function, is what a cancel path is
/// matched with, so an id that is accepted here can always be named there.
/// </summary>
public sealed class RequestIdTests
{
    /// <summary>The rest of a body after its <c>requestId</c> member: a valid selection, and the end.</summary>
    private const string Selection =
        "\"assistant\":{\"model\":\"gpt-test\",\"reasoningEffort\":\"low\"},\"transcript\":\"x\"}";

    /// <summary>The value of <c>requestId</c> as JSON text; null leaves the member out.</summary>
    public static TheoryData<string?> BadIds => new()
    {
        { null },                                   // absent
        { "7" },                                    // a number
        { "\"\"" },                                 // empty
        { "\"" + new string('a', 81) + "\"" },      // 81 characters
        { "\"a/b\"" },                              // a slash
        { "\"a b\"" },                              // a space
        { "null" },
        { "true" },
        { "[\"request-0001\"]" },
        { "{\"id\":\"request-0001\"}" },
        { "\" request-0001\"" },
        { "\"request-0001 \"" },
        { "\"request.0001\"" },
        { "\"request:0001\"" },
        { "\"a\\\\b\"" },                           // a backslash
        { "\"a%2Fb\"" },
        { "\"..\"" },
        { "\"--disable\\u0000\"" },
        { "\"requête\"" },
        { "\"" + new string('a', 10_000) + "\"" },
    };

    [Theory]
    [MemberData(nameof(BadIds))]
    public async Task Bad_ids_are_400_on_both_routes(string? idJson)
    {
        await using var host = await RelayTestHost.StartAsync();
        var body = idJson is null ? "{" + Selection : "{\"requestId\":" + idJson + "," + Selection;

        using var assist = await host.PostAsync(AnswerTesting.Assist, body);
        using var summary = await host.PostAsync(AnswerTesting.Summary, body);

        await AnswerTesting.AssertErrorAsync(assist, RelayErrors.InvalidRequestId);
        await AnswerTesting.AssertErrorAsync(summary, RelayErrors.InvalidRequestId);
        Assert.Equal(
            """{"error":"A valid requestId is required.","code":"INVALID_REQUEST_ID"}""",
            await assist.Content.ReadAsStringAsync());
        Assert.Empty(host.Backend.Runs);
        Assert.Equal(0, host.Catalog.Calls);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("rn")]
    [InlineData("u2028")]
    [InlineData("u2029")]
    [InlineData("u0085")]
    [InlineData("t")]
    public async Task An_id_with_a_trailing_line_break_is_400(string escape)
    {
        await using var host = await RelayTestHost.StartAsync();

        // The escape is written into the JSON text, so the id itself ends in the line break.
        var ending = escape == "rn" ? "\\r\\n" : "\\" + escape;
        var body = "{\"requestId\":\"request-0001" + ending + "\"," + Selection;
        var leading = "{\"requestId\":\"" + ending + "request-0001\"," + Selection;

        using var assist = await host.PostAsync(AnswerTesting.Assist, body);
        using var summary = await host.PostAsync(AnswerTesting.Summary, body);
        using var atTheStart = await host.PostAsync(AnswerTesting.Assist, leading);

        await AnswerTesting.AssertErrorAsync(assist, RelayErrors.InvalidRequestId);
        await AnswerTesting.AssertErrorAsync(summary, RelayErrors.InvalidRequestId);
        await AnswerTesting.AssertErrorAsync(atTheStart, RelayErrors.InvalidRequestId);
        Assert.Empty(host.Backend.Runs);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("6F9619FF-8B86-4011-B42D-00C04FC964FF")]
    [InlineData("request_0001-Az09")]
    [InlineData("-")]
    [InlineData("_")]
    [InlineData("--disable")]
    public async Task An_id_of_the_shape_is_accepted_and_is_the_id_of_the_run(string id)
    {
        await using var host = await RelayTestHost.StartAsync();
        var eighty = new string('a', ProtocolConstants.RequestIdMaxLength);

        using var response = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist(id));
        using var longest = await host.PostAsync(AnswerTesting.Summary, Bodies.Summary(eighty));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
        Assert.Equal([id, eighty], host.Backend.Runs.Select(run => run.RequestId));
        Assert.Equal(80, ProtocolConstants.RequestIdMaxLength);

        // The same text is a cancel path: both shapes are one.
        Assert.True(LiveCue.Relay.Edge.RouteTable.IsRequestIdShape(id));
        Assert.Equal(id, MatchedId("/v1/requests/" + id));
        Assert.Equal(eighty, MatchedId("/v1/requests/" + eighty));
    }

    private static string? MatchedId(string target)
    {
        _ = LiveCue.Relay.Edge.RouteTable.Match("DELETE", target, out var requestId);
        return requestId;
    }
}
