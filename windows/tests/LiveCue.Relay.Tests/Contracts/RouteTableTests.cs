using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Contracts;

/// <summary>Step E-4 as a pure function: the ten rows of protocol 6.1, route 9's pattern, exact matching.</summary>
public sealed class RouteTableTests
{
    [Theory]
    [InlineData("GET", "/v1/health", 1)]
    [InlineData("POST", "/v1/pair/verify", 2)]
    [InlineData("GET", "/v1/models", 3)]
    [InlineData("GET", "/v1/local-model", 4)]
    [InlineData("POST", "/v1/local-model/start", 5)]
    [InlineData("POST", "/v1/local-model/stop", 6)]
    [InlineData("POST", "/v1/assist", 7)]
    [InlineData("POST", "/v1/session-summary", 8)]
    [InlineData("DELETE", "/v1/requests/abc", 9)]
    [InlineData("DELETE", "/v1/requests/A-b_9", 9)]
    [InlineData("GET", "/v1/speech", 10)]
    [InlineData("GET", "/v1/admin", null)]
    [InlineData("GET", "/v1/health?token=x", null)]
    [InlineData("GET", "/v1/%68ealth", null)]
    [InlineData("GET", "/v1/health/", null)]
    [InlineData("POST", "/v1/health", null)]
    [InlineData("HEAD", "/v1/health", null)]
    [InlineData("get", "/v1/health", null)]
    [InlineData("GET", "/V1/HEALTH", null)]
    [InlineData("GET", "//v1/health", null)]
    [InlineData("GET", "/livecue/v1/health", null)]
    [InlineData("POST", "/desktop/pair", null)]
    [InlineData("GET", "http://127.0.0.1/v1/health", null)]
    [InlineData("GET", "", null)]
    [InlineData("POST", "/v1/speech", null)]
    [InlineData("DELETE", "/v1/requests/", null)]
    [InlineData("DELETE", "/v1/requests", null)]
    [InlineData("DELETE", "/v1/requests/a%2Fb", null)]
    [InlineData("DELETE", "/v1/requests/a/b", null)]
    [InlineData("DELETE", "/v1/requests/a b", null)]
    [InlineData("DELETE", "/v1/requests/a?x=1", null)]
    [InlineData("GET", "/v1/requests/abc", null)]
    public void Match_cases(string method, string target, int? expectedRoute)
    {
        var route = RouteTable.Match(method, target, out var requestId);

        Assert.Equal(expectedRoute, (int?)route?.Id);
        if (route?.Id == RouteId.CancelRequest)
        {
            Assert.Equal(target["/v1/requests/".Length..], requestId);
        }
        else
        {
            Assert.Null(requestId);
        }
    }

    [Fact]
    public void A_request_id_is_one_to_eighty_characters()
    {
        var eighty = new string('a', ProtocolConstants.RequestIdMaxLength);

        Assert.Equal(RouteId.CancelRequest, RouteTable.Match("DELETE", "/v1/requests/" + eighty, out var id)?.Id);
        Assert.Equal(eighty, id);
        Assert.Null(RouteTable.Match("DELETE", "/v1/requests/" + eighty + "a", out _));
        Assert.True(RouteTable.IsRequestIdShape("a"));
        Assert.False(RouteTable.IsRequestIdShape(string.Empty));
        Assert.False(RouteTable.IsRequestIdShape("a.b"));
    }

    [Fact]
    public void The_ten_rows_carry_the_flags_and_deadlines_of_the_protocol()
    {
        var rows = RouteTable.Rows;

        Assert.Equal(Enumerable.Range(1, 10), rows.Select(row => (int)row.Id));
        Assert.Equal(
            [RouteId.PairVerify, RouteId.LocalModelStart, RouteId.LocalModelStop, RouteId.Assist, RouteId.SessionSummary],
            rows.Where(row => row.ReadsBody).Select(row => row.Id));
        Assert.Equal(
            [RouteId.Health, RouteId.PairVerify, RouteId.Models, RouteId.LocalModelStatus, RouteId.CancelRequest],
            rows.Where(row => row.ServedWhilePaused).Select(row => row.Id));
        Assert.Equal(ProtocolConstants.ModelsDeadline, RouteTable.Row(RouteId.Models).Deadline);
        Assert.Equal(ProtocolConstants.AnswerDeadline, RouteTable.Row(RouteId.Assist).Deadline);
        Assert.Equal(ProtocolConstants.AnswerDeadline, RouteTable.Row(RouteId.SessionSummary).Deadline);
        Assert.Equal(ProtocolConstants.CancelDeadline, RouteTable.Row(RouteId.CancelRequest).Deadline);
        Assert.Equal(Timeout.InfiniteTimeSpan, RouteTable.Row(RouteId.Speech).Deadline);
    }
}
