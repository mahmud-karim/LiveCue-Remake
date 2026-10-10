using System.Text;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-009 (step E-6: a route that reads no body is sent none) and P2-013 (protocol 5.4 rule 4:
/// a body is one JSON object).
/// </summary>
public sealed class BodyRuleTests : IAsyncLifetime
{
    private RelayTestHost _host = null!;

    public async Task InitializeAsync() => _host = await RelayTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("GET /v1/health HTTP/1.1")]
    [InlineData("DELETE /v1/requests/x HTTP/1.1")]
    [InlineData("GET /v1/models HTTP/1.1")]
    [InlineData("GET /v1/local-model HTTP/1.1")]
    public async Task Get_and_delete_with_a_body_are_400(string requestLine)
    {
        // A length above zero, and a transfer coding (which declares a body even when it is empty).
        var withLength = await ExchangeAsync(_host.RawHead(requestLine, ("Content-Length", "2")) + "{}");
        var chunked = await ExchangeAsync(_host.RawHead(requestLine, ("Transfer-Encoding", "chunked")) + "2\r\n{}\r\n0\r\n\r\n");
        var emptyChunked = await ExchangeAsync(_host.RawHead(requestLine, ("Transfer-Encoding", "chunked")) + "0\r\n\r\n");

        foreach (var response in (RawResponse?[])[withLength, chunked, emptyChunked])
        {
            Assert.NotNull(response);
            Assert.Equal(400, response.Status);
            Assert.Equal(Wire.InvalidRequest, response.Body);

            // The answer came before the body was read, so the connection is not kept (W-design K-4).
            Assert.Equal("close", response.Header("Connection"));
        }

        // A declared length of zero is no body.
        var zero = await ExchangeAsync(_host.RawHead(requestLine, ("Content-Length", "0")));
        Assert.NotEqual(Wire.InvalidRequest, zero!.Body);
        Assert.NotEqual(400, zero.Status);

        // The route's own work never began for the refused ones.
        Assert.True(_host.Catalog.Calls <= 1);
        Assert.True(_host.Lab.ProbeCalls <= 1);
    }

    public static TheoryData<string, string> EveryBodyRouteAndBadBody()
    {
        var cases = new TheoryData<string, string>();
        foreach (var route in Wire.HttpRoutes.Where(route => route.ReadsBody))
        {
            foreach (var body in (string[])["[]", "null", "\"x\"", "{", string.Empty])
            {
                cases.Add(route.Target, body);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(EveryBodyRouteAndBadBody))]
    public async Task Non_object_and_empty_bodies_are_400_on_every_body_route(string target, string body)
    {
        using var response = await _host.PostAsync(target, body);
        await _host.Events.SettleAsync();

        await Wire.AssertErrorAsync(response, 400, "INVALID_JSON");
        Assert.Empty(_host.Backend.Runs);
        Assert.Equal(0, _host.Lab.StartCalls);
        Assert.Equal(0, _host.Lab.StopCalls);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("12")]
    [InlineData("{} {}")]
    [InlineData("{}x")]
    [InlineData("{\"a\":1,}")]
    [InlineData("{\"a\":1} // comment")]
    [InlineData("/* comment */ {}")]
    [InlineData("{'a':1}")]
    [InlineData(" ")]
    public async Task Anything_but_one_strict_json_object_is_400(string body)
    {
        using var response = await _host.PostAsync("/v1/pair/verify", body);

        await Wire.AssertErrorAsync(response, 400, "INVALID_JSON");
    }

    [Fact]
    public async Task Invalid_utf8_a_byte_order_mark_and_nesting_beyond_32_are_400_and_32_levels_pass()
    {
        static byte[] Nested(int depth) =>
            Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("{\"a\":", depth - 1)) + "{}" + new string('}', depth - 1));

        using var invalidUtf8 = await PostBytesAsync([(byte)'{', (byte)'"', 0xFF, (byte)'"', (byte)':', (byte)'1', (byte)'}']);
        using var mark = await PostBytesAsync([0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']);
        using var tooDeep = await PostBytesAsync(Nested(33));
        using var deepest = await PostBytesAsync(Nested(32));
        using var surroundedByWhiteSpace = await PostBytesAsync(Encoding.ASCII.GetBytes(" \r\n\t{} \n"));

        await Wire.AssertErrorAsync(invalidUtf8, 400, "INVALID_JSON");
        await Wire.AssertErrorAsync(mark, 400, "INVALID_JSON");
        await Wire.AssertErrorAsync(tooDeep, 400, "INVALID_JSON");
        Assert.Equal("""{"ok":true}""", await deepest.Content.ReadAsStringAsync());
        Assert.Equal("""{"ok":true}""", await surroundedByWhiteSpace.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_content_type_of_the_request_is_not_checked()
    {
        var request = _host.Request(HttpMethod.Post, "/v1/pair/verify");
        request.Content = new StringContent("{}", Encoding.UTF8, "text/plain");
        using var plain = await _host.SendAsync(request);

        var none = await ExchangeAsync(_host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "2")) + "{}");

        Assert.Equal("""{"ok":true}""", await plain.Content.ReadAsStringAsync());
        Assert.Equal("""{"ok":true}""", none!.Body);
    }

    private Task<HttpResponseMessage> PostBytesAsync(byte[] body)
    {
        var request = _host.Request(HttpMethod.Post, "/v1/pair/verify");
        request.Content = new ByteArrayContent(body);
        return _host.SendAsync(request);
    }

    private async Task<RawResponse?> ExchangeAsync(string request)
    {
        using var raw = await _host.OpenRawAsync();
        await raw.SendAsync(request);
        return await raw.ReadResponseAsync();
    }
}
