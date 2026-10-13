using LiveCue.Hosting.Tailscale;
using LiveCue.Hosting.Tests.Support;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>The reader of <c>tailscale status --json</c>: online, and the DNS name of this PC.</summary>
public sealed class TailscaleStatusParserTests
{
    [Fact]
    public void Online_and_name_rules()
    {
        // Online is: BackendState "Running" and Self.Online true. The name loses its dot and its capitals.
        Assert.Equal(new TailscaleNodeStatus(true, "pc.example.test"), Parse(ServeStatusFixtures.StatusOnline));
        Assert.Equal(new TailscaleNodeStatus(true, "pc.example.test"), Parse(TailscaleFixtures.StatusRunning));
        Assert.Equal(new TailscaleNodeStatus(false, "pc.example.test"), Parse(ServeStatusFixtures.StatusStopped));
        Assert.Equal(new TailscaleNodeStatus(true, null), Parse(ServeStatusFixtures.StatusWithoutName));

        // Either half alone is not online.
        Assert.False(Parse("""{"BackendState":"Running","Self":{"Online":false,"DNSName":"pc.example.test."}}""").Online);
        Assert.False(Parse("""{"BackendState":"Starting","Self":{"Online":true,"DNSName":"pc.example.test."}}""").Online);
        Assert.False(Parse("""{"BackendState":"running","Self":{"Online":true}}""").Online);
        Assert.False(Parse("""{"BackendState":"Running","Self":{"DNSName":"pc.example.test."}}""").Online);

        // A PC that is signed out has no "Self": a readable document of a PC that is offline.
        Assert.Equal(new TailscaleNodeStatus(false, null), Parse("""{"BackendState":"NeedsLogin"}"""));
        Assert.Equal(new TailscaleNodeStatus(false, null), Parse("""{"BackendState":"NeedsLogin","Self":null}"""));
        Assert.Equal(new TailscaleNodeStatus(true, null), Parse("""{"BackendState":"Running","Self":{"Online":true,"DNSName":null}}"""));
    }

    [Theory]
    [InlineData("pc.example.test.", "pc.example.test")]
    [InlineData("pc.example.test", "pc.example.test")]
    [InlineData("PC-1.Example.Test.", "pc-1.example.test")]
    [InlineData("pc", "pc")]
    [InlineData("", null)]
    [InlineData(".", null)]
    [InlineData("pc.example.test..", null)]
    [InlineData("pc..example.test.", null)]
    [InlineData("-pc.example.test.", null)]
    [InlineData("pc-.example.test.", null)]
    [InlineData("pc_name.example.test.", null)]
    [InlineData("pc name.example.test.", null)]
    [InlineData("pc.example.test:443", null)]
    [InlineData("pc.example.test/path", null)]
    [InlineData("pc.example.test\n", null)]
    [InlineData("pç.example.test.", null)]
    public void A_name_that_is_not_a_host_of_the_protocol_is_no_name(string reported, string? expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            BackendState = "Running",
            Self = new { Online = true, DNSName = reported },
        });

        Assert.Equal(new TailscaleNodeStatus(true, expected), Parse(json));
    }

    [Fact]
    public void Labels_and_the_whole_name_have_a_length_limit()
    {
        var label63 = new string('a', 63);
        var name253 = string.Join('.', label63, label63, label63, new string('b', 61));

        Assert.Equal(label63 + ".test", TailscaleStatusParser.NormalizeDnsName(label63 + ".test."));
        Assert.Null(TailscaleStatusParser.NormalizeDnsName(label63 + "a.test."));
        Assert.Equal(253, name253.Length);
        Assert.Equal(name253, TailscaleStatusParser.NormalizeDnsName(name253 + "."));
        Assert.Null(TailscaleStatusParser.NormalizeDnsName(name253 + "b."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"Self":{"Online":true,"DNSName":"pc.example.test."}}""")]
    [InlineData("""{"BackendState":7,"Self":{"Online":true}}""")]
    [InlineData("""{"BackendState":"Running","Self":[]}""")]
    [InlineData("""{"BackendState":"Running","Self":{"Online":"true"}}""")]
    [InlineData("""{"BackendState":"Running","Self":{"Online":true,"DNSName":7}}""")]
    [InlineData("""{"BackendState":"Running","Self":{"Online":true""")]
    public void A_document_of_another_shape_is_unparseable(string document)
    {
        Assert.False(TailscaleStatusParser.TryParse(document, out var status));
        Assert.Null(status);
    }

    private static TailscaleNodeStatus Parse(string document)
    {
        Assert.True(TailscaleStatusParser.TryParse(document, out var status), document);
        return status;
    }
}
