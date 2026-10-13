using System.Text;
using LiveCue.Hosting.Tailscale;
using LiveCue.Hosting.Tests.Support;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// The reader of <c>tailscale serve status --json</c> (W-design 4.15). It fails closed: anything that is
/// not the expected shape is unparseable, and rule TS-1 then writes nothing.
/// </summary>
public sealed class ServeStatusParserTests
{
    [Fact]
    public void Fixtures()
    {
        // An empty object: nothing is configured.
        var empty = Parse(ServeStatusFixtures.Empty);
        Assert.Empty(empty.Handlers);
        Assert.Empty(empty.MentionedPorts);
        Assert.Empty(empty.TcpForwardPorts);
        Assert.Empty(empty.FunnelPorts);
        Assert.Equal([Leaf(string.Empty, "{}")], empty.Leaves);

        // One private handler.
        var one = Parse(ServeStatusFixtures.PrivateHandler());
        Assert.Equal([new ServeHandler(11443, "/", "Proxy", "http://127.0.0.1:47841", Foreground: false)], one.Handlers);
        Assert.Equal([11443], one.MentionedPorts);
        Assert.Empty(one.FunnelPorts);
        Assert.Equal(
            [
                Leaf("/TCP/11443/HTTPS", "true"),
                Leaf("/Web/pc.example.test:11443/Handlers/~1/Proxy", "\"http://127.0.0.1:47841\""),
            ],
            one.Leaves);

        // A Funnel port with several mounts.
        var funnel = Parse(ServeStatusFixtures.FunnelWithMounts());
        Assert.Equal(
            [
                new ServeHandler(10000, "/livecue", "Proxy", "http://127.0.0.1:47841", Foreground: false),
                new ServeHandler(10000, "/other", "Proxy", "http://127.0.0.1:3000", Foreground: false),
            ],
            funnel.Handlers);
        Assert.Equal([10000], funnel.MentionedPorts);
        Assert.Equal([10000], funnel.FunnelPorts);
        Assert.Contains(Leaf("/AllowFunnel/pc.example.test:10000", "true"), funnel.Leaves);
        Assert.Contains(Leaf("/Web/pc.example.test:10000/Handlers/~1livecue/Proxy", "\"http://127.0.0.1:47841\""), funnel.Leaves);

        // TCP forwarding.
        var tcp = Parse(ServeStatusFixtures.TcpForward());
        Assert.Empty(tcp.Handlers);
        Assert.Equal([10000], tcp.MentionedPorts);
        Assert.Equal([10000], tcp.TcpForwardPorts);

        // A foreground session: its handler is marked, its port is mentioned.
        var foreground = Parse(ServeStatusFixtures.ForegroundSession());
        Assert.Equal([new ServeHandler(11443, "/", "Proxy", "http://127.0.0.1:8080", Foreground: true)], foreground.Handlers);
        Assert.Equal([11443], foreground.MentionedPorts);

        // Services: read with the same shapes, and marked like a foreground session.
        var service = Parse(ServeStatusFixtures.Service());
        Assert.Equal([new ServeHandler(443, "/", "Text", "hello", Foreground: true)], service.Handlers);
        Assert.Equal([443], service.MentionedPorts);

        // A non-object.
        Assert.False(ServeStatusParser.TryParse(ServeStatusFixtures.NotAnObject, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void Several_paths_and_every_kind_of_handler_on_one_port()
    {
        var snapshot = Parse(TailscaleFixtures.TailnetOnly443);

        Assert.Equal(
            [
                new ServeHandler(443, "/", "Proxy", "http://127.0.0.1:3000", Foreground: false),
                new ServeHandler(443, "/api/", "Proxy", "http://localhost:8080", Foreground: false),
                new ServeHandler(443, "/docs", "Path", @"C:\Sites\docs", Foreground: false),
                new ServeHandler(443, "/hello", "Text", "hello", Foreground: false),
                new ServeHandler(443, "/old", "Redirect", "https://pc.example.test/new", Foreground: false),
            ],
            snapshot.Handlers);
        Assert.Equal([443], snapshot.MentionedPorts);
        Assert.Empty(snapshot.FunnelPorts);
        Assert.Empty(snapshot.TcpForwardPorts);
    }

    [Fact]
    public void A_shared_configuration_is_read_in_full()
    {
        var snapshot = Parse(TailscaleFixtures.SharedPortsAndTheRelay);

        Assert.Equal([443, 5432, 8443, 10000], snapshot.MentionedPorts);
        Assert.Equal([5432], snapshot.TcpForwardPorts);
        Assert.Equal([443, 10000], snapshot.FunnelPorts);
        Assert.Equal(
            [(443, "/"), (443, "/files"), (8443, "/"), (10000, "/livecue")],
            snapshot.Handlers.Select(handler => (handler.Port, handler.Mount)));

        // Leaves are the whole document, sorted by pointer.
        Assert.Equal(10, snapshot.Leaves.Count);
        Assert.Equal(snapshot.Leaves.Select(leaf => leaf.Key).Order(StringComparer.Ordinal), snapshot.Leaves.Select(leaf => leaf.Key));
        Assert.Contains(Leaf("/TCP/5432/TCPForward", "\"127.0.0.1:5432\""), snapshot.Leaves);
    }

    [Fact]
    public void Funnel_ports_are_the_entries_that_are_true_and_ports_are_collected_from_every_structure()
    {
        var snapshot = Parse(ServeDoc.New()
            .Proxy(443, "/", 3000)
            .Funnel(443, on: false)
            .Funnel(8443)
            .TcpForward(2222)
            .Foreground("session-1", ServeDoc.New().Proxy(10000, "/", 47841).Funnel(10000))
            .Service("svc:example", ServeDoc.New().Proxy(444, "/", 3000, host: "example.example.test").TcpForward(5555))
            .Json());

        Assert.Equal([443, 444, 2222, 5555, 8443, 10000], snapshot.MentionedPorts);
        Assert.Equal([8443, 10000], snapshot.FunnelPorts);
        Assert.Equal([2222, 5555], snapshot.TcpForwardPorts);
        Assert.Equal(
            [
                new ServeHandler(443, "/", "Proxy", "http://127.0.0.1:3000", Foreground: false),
                new ServeHandler(444, "/", "Proxy", "http://127.0.0.1:3000", Foreground: true),
                new ServeHandler(10000, "/", "Proxy", "http://127.0.0.1:47841", Foreground: true),
            ],
            snapshot.Handlers);
    }

    [Fact]
    public void What_is_unknown_is_kept_in_the_leaves_and_changes_no_rule()
    {
        // A member a later Tailscale might add, a handler of a kind this reader does not know, a handler
        // without content, a port key of an address in brackets, and a mount that needs escaping.
        var snapshot = Parse("""
            {
              "Later": { "Setting": [1, "two", null, {}] },
              "Web": {
                "[::1]:8443": {
                  "Handlers": {
                    "/a~b/c": { "Proxy": "http://127.0.0.1:47841", "Path": "ignored" },
                    "/new": { "Later": true },
                    "/none": null
                  }
                },
                "pc.example.test:9443": { "Handlers": null },
                "pc.example.test:9444": {}
              }
            }
            """);

        Assert.Equal(
            [
                new ServeHandler(8443, "/a~b/c", "Proxy", "http://127.0.0.1:47841", Foreground: false),
                new ServeHandler(8443, "/new", "Other", string.Empty, Foreground: false),
                new ServeHandler(8443, "/none", "Other", string.Empty, Foreground: false),
            ],
            snapshot.Handlers);
        Assert.Equal([8443, 9443, 9444], snapshot.MentionedPorts);
        Assert.Contains(Leaf("/Later/Setting/0", "1"), snapshot.Leaves);
        Assert.Contains(Leaf("/Later/Setting/1", "\"two\""), snapshot.Leaves);
        Assert.Contains(Leaf("/Later/Setting/2", "null"), snapshot.Leaves);
        Assert.Contains(Leaf("/Later/Setting/3", "{}"), snapshot.Leaves);
        Assert.Contains(Leaf("/Web/[::1]:8443/Handlers/~1a~0b~1c/Proxy", "\"http://127.0.0.1:47841\""), snapshot.Leaves);
        Assert.Contains(Leaf("/Web/pc.example.test:9443/Handlers", "null"), snapshot.Leaves);
        Assert.Contains(Leaf("/Web/pc.example.test:9444", "{}"), snapshot.Leaves);
    }

    [Fact]
    public void Malformed_or_unexpected_documents_are_unparseable()
    {
        Assert.All(TailscaleFixtures.Malformed, document =>
        {
            Assert.False(ServeStatusParser.TryParse(document, out var snapshot), document);
            Assert.Null(snapshot);
        });

        // Bytes that are not UTF-8, and text that is not valid once unescaped.
        Assert.False(ServeStatusParser.TryParse(new byte[] { 0x7B, 0x22, 0xFF, 0xFE, 0x22, 0x3A, 0x31, 0x7D }, out _));
        Assert.False(ServeStatusParser.TryParse("{\"Web\": {\"pc.example.test:443\": {\"Handlers\": {\"/\\ud800\": {}}}}}", out _));
    }

    [Fact]
    public void A_byte_order_mark_and_a_final_line_feed_are_accepted()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(ServeStatusFixtures.PrivateHandler() + "\n")];

        Assert.True(ServeStatusParser.TryParse(bytes, out var snapshot));
        Assert.Single(snapshot.Handlers);
    }

    [Fact]
    public void The_same_text_has_one_spelling_in_the_leaves()
    {
        var plain = Parse("""{"Web": {"pc.example.test:443": {"Handlers": {"/": {"Proxy": "http://127.0.0.1:47841"}}}}}""");
        var escaped = Parse("""{"Web": {"pc.example.test:443": {"Handlers": {"/": {"Proxy": "http:\/\/127.0.0.1:47841"}}}}}""");

        Assert.Equal(plain.Leaves, escaped.Leaves);
        Assert.Equal(plain.Handlers, escaped.Handlers);
    }

    private static KeyValuePair<string, string> Leaf(string pointer, string value) => new(pointer, value);

    private static ServeSnapshot Parse(string document)
    {
        Assert.True(ServeStatusParser.TryParse(document, out var snapshot), document);
        return snapshot;
    }
}
