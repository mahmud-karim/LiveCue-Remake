using System.Text;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>The pairing payload of protocol 4.2: one exact text, and the bytes the QR code carries.</summary>
public sealed class PairingPayloadTests
{
    // Protocol P2-142, second half: the example of protocol 4.2, character for character.
    [Fact]
    public void The_payload_is_the_exact_text_of_the_protocol()
    {
        var bytes = PairingPayload.Utf8("https://pc.example.test:11443/", Encoding.ASCII.GetBytes(TestTokens.Example));

        Assert.Equal(
            """{"endpoint":"https://pc.example.test:11443/","token":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""",
            PairingPayload.Text(bytes));
        Assert.Equal(
            Encoding.UTF8.GetBytes(
                """{"endpoint":"https://pc.example.test:11443/","token":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}"""),
            bytes);
    }

    [Fact]
    public void A_path_prefix_is_carried_as_it_is_advertised()
    {
        var bytes = PairingPayload.Utf8("https://pc.example.test:10000/livecue/", Encoding.ASCII.GetBytes(TestTokens.Example));

        Assert.Equal(
            """{"endpoint":"https://pc.example.test:10000/livecue/","token":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""",
            PairingPayload.Text(bytes));
    }

    // Both values must need no JSON escaping, or the hand-written text would not be exact.
    [Theory]
    [InlineData("https://pc.example.test:11443")]
    [InlineData("https://PC.example.test:11443/")]
    [InlineData("https://pc.example.test/\"/")]
    [InlineData("http://pc.example.test/")]
    public void An_endpoint_that_is_not_canonical_is_refused(string endpoint)
    {
        Assert.Throws<ArgumentException>(
            () => PairingPayload.Utf8(endpoint, Encoding.ASCII.GetBytes(TestTokens.Example)));
    }

    [Theory]
    [InlineData(42)]
    [InlineData(44)]
    public void Bytes_that_are_not_a_token_are_refused(int length)
    {
        Assert.Throws<ArgumentException>(
            () => PairingPayload.Utf8(RelayTestHost.Endpoint, Encoding.ASCII.GetBytes(TestTokens.OfLength(length))));
        Assert.Throws<ArgumentException>(
            () => PairingPayload.Utf8(RelayTestHost.Endpoint, Encoding.ASCII.GetBytes(new string('"', 43))));
    }
}
