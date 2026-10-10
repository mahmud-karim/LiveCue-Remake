namespace LiveCue.Hosting.Tests.Support;

/// <summary>
/// Documents as the two Tailscale status commands print them, for the parsers and the mapping manager.
/// Every name is on a reserved test domain; nothing here was copied from a real machine.
/// </summary>
internal static class ServeStatusFixtures
{
    /// <summary>The host of every fixture, as "tailscale status" reports it (with the trailing dot).</summary>
    public const string DnsNameWithDot = "pc.example.test.";

    public const string DnsName = "pc.example.test";

    /// <summary><c>tailscale serve status --json</c> with nothing configured.</summary>
    public const string Empty = "{}";

    /// <summary>Not an object: the parser must call it unparseable.</summary>
    public const string NotAnObject = "[]";

    /// <summary><c>tailscale status --json</c> of a connected PC.</summary>
    public const string StatusOnline =
        """{"BackendState":"Running","Self":{"Online":true,"DNSName":"pc.example.test.","HostName":"pc"}}""";

    /// <summary><c>tailscale status --json</c> of a PC whose Tailscale is stopped.</summary>
    public const string StatusStopped =
        """{"BackendState":"Stopped","Self":{"Online":false,"DNSName":"pc.example.test.","HostName":"pc"}}""";

    /// <summary><c>tailscale status --json</c> of a PC that is running without a name.</summary>
    public const string StatusWithoutName =
        """{"BackendState":"Running","Self":{"Online":true,"DNSName":"","HostName":"pc"}}""";

    /// <summary>One tailnet-only handler at the root of <paramref name="httpsPort"/>, proxying to the relay: our Private mapping.</summary>
    public static string PrivateHandler(int httpsPort = 11443, int relayPort = 47841) => $$"""
        {
          "TCP": { "{{httpsPort}}": { "HTTPS": true } },
          "Web": {
            "pc.example.test:{{httpsPort}}": {
              "Handlers": { "/": { "Proxy": "http://127.0.0.1:{{relayPort}}" } }
            }
          }
        }
        """;

    /// <summary>A Funnel port with our mount and one mount of another program: our Public mapping beside a neighbour.</summary>
    public static string FunnelWithMounts(int httpsPort = 10000, string prefix = "/livecue", int relayPort = 47841) => $$"""
        {
          "TCP": { "{{httpsPort}}": { "HTTPS": true } },
          "Web": {
            "pc.example.test:{{httpsPort}}": {
              "Handlers": {
                "{{prefix}}": { "Proxy": "http://127.0.0.1:{{relayPort}}" },
                "/other": { "Proxy": "http://127.0.0.1:3000" }
              }
            }
          },
          "AllowFunnel": { "pc.example.test:{{httpsPort}}": true }
        }
        """;

    /// <summary>A tailnet-only handler of another program on <paramref name="httpsPort"/>, with Funnel off.</summary>
    public static string ForeignHandler(int httpsPort = 10000) => $$"""
        {
          "TCP": { "{{httpsPort}}": { "HTTPS": true } },
          "Web": {
            "pc.example.test:{{httpsPort}}": {
              "Handlers": { "/": { "Proxy": "http://127.0.0.1:3000" } }
            }
          }
        }
        """;

    /// <summary>Raw TCP forwarding on <paramref name="port"/>.</summary>
    public static string TcpForward(int port = 10000) => $$"""
        {
          "TCP": { "{{port}}": { "TCPForward": "127.0.0.1:5432" } }
        }
        """;

    /// <summary>A foreground session that serves <paramref name="httpsPort"/>.</summary>
    public static string ForegroundSession(int httpsPort = 11443) => $$"""
        {
          "Foreground": {
            "session-1": {
              "TCP": { "{{httpsPort}}": { "HTTPS": true } },
              "Web": {
                "pc.example.test:{{httpsPort}}": {
                  "Handlers": { "/": { "Proxy": "http://127.0.0.1:8080" } }
                }
              }
            }
          }
        }
        """;

    /// <summary>A service that serves <paramref name="httpsPort"/>.</summary>
    public static string Service(int httpsPort = 443) => $$"""
        {
          "Services": {
            "svc:example": {
              "TCP": { "{{httpsPort}}": { "HTTPS": true } },
              "Web": {
                "example.example.test:{{httpsPort}}": {
                  "Handlers": { "/": { "Text": "hello" } }
                }
              }
            }
          }
        }
        """;
}
