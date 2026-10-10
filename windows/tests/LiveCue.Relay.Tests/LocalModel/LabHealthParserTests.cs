using System.Text;
using LiveCue.Relay.LocalModel;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// Protocol 11.2 and 9.2 without a socket: which health replies the relay reads at all, and which of those
/// it calls usable.
/// </summary>
public sealed class LabHealthParserTests
{
    [Fact]
    public void The_reply_of_the_protocol_is_read()
    {
        var health = Parse(
            """{"model":"nemotron","ready":true,"loadSeconds":41.7,"sampleRate":16000,"maxSeconds":120,"chunkMs":320,"relayProtocol":1,"busy":false}""");

        Assert.Equal(new LabHealth(Ready: true, "nemotron", Busy: false, RelayProtocol: 1), health);
    }

    [Theory]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":1}""", true, null, false, 1)]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":1,"model":null}""", true, null, false, 1)]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":1,"model":["nemotron"]}""", true, null, false, 1)]
    [InlineData("""{"ready":false,"busy":true,"relayProtocol":0,"model":"qwen3"}""", false, "qwen3", true, 0)]
    [InlineData("""{"ready":true,"busy":true,"relayProtocol":-7,"model":"x"}""", true, "x", true, -7)]
    [InlineData("""  {"ready":true,"busy":false,"relayProtocol":2147483647,"model":""}  """, true, "", false, int.MaxValue)]
    public void Only_the_four_members_matter(string json, bool ready, string? model, bool busy, int relayProtocol)
    {
        Assert.Equal(new LabHealth(ready, model, busy, relayProtocol), Parse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":1} trailing""")]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":1,}""")]
    [InlineData("""{"ready":1,"busy":false,"relayProtocol":1}""")]
    [InlineData("""{"ready":"true","busy":false,"relayProtocol":1}""")]
    [InlineData("""{"ready":null,"busy":false,"relayProtocol":1}""")]
    [InlineData("""{"ready":true,"busy":"false","relayProtocol":1}""")]
    [InlineData("""{"ready":true,"busy":null,"relayProtocol":1}""")]
    [InlineData("""{"ready":true,"busy":false}""")]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":"1"}""")]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":true}""")]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":null}""")]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":1.5}""")]
    [InlineData("""{"ready":true,"busy":false,"relayProtocol":99999999999}""")]
    [InlineData("""{"Ready":true,"Busy":false,"RelayProtocol":1}""")]
    public void Anything_else_is_no_lab(string json)
    {
        Assert.Null(Parse(json));
    }

    [Fact]
    public void A_reply_above_the_probe_limit_is_no_lab()
    {
        var padded = "{\"ready\":true,\"busy\":false,\"relayProtocol\":1,\"pad\":\"" + new string('p', 4096) + "\"}";

        Assert.Null(Parse(padded));
        Assert.NotNull(Parse("""{"ready":true,"busy":false,"relayProtocol":1}"""));
    }

    [Theory]
    [InlineData(true, "nemotron", 1, true)]
    [InlineData(true, "qwen3", 1, true)]
    [InlineData(false, "nemotron", 1, false)]
    [InlineData(true, "nemotron", 2, false)]
    [InlineData(true, "nemotron", 0, false)]
    [InlineData(true, "Nemotron", 1, false)]
    [InlineData(true, "nemotron ", 1, false)]
    [InlineData(true, "whisper", 1, false)]
    [InlineData(true, "", 1, false)]
    [InlineData(true, null, 1, false)]
    public void Usable_is_ready_with_protocol_one_and_a_model_of_c04(bool ready, string? model, int relayProtocol, bool expected)
    {
        // "busy" has no part in it: a usable lab that is busy is still the loaded model.
        foreach (var busy in new[] { false, true })
        {
            var usable = LabHealthParser.IsUsable(new LabHealth(ready, model, busy, relayProtocol), out var loaded);

            Assert.Equal(expected, usable);
            Assert.Equal(expected ? model : string.Empty, loaded);
        }

        Assert.False(LabHealthParser.IsUsable(null, out _));
    }

    private static LabHealth? Parse(string json) => LabHealthParser.Parse(Encoding.UTF8.GetBytes(json));
}
