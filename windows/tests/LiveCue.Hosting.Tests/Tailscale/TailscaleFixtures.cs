using System.Globalization;
using System.Text.Json.Nodes;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// Documents as <c>tailscale serve status --json</c> and <c>tailscale status --json</c> print them,
/// written by hand from the shapes Tailscale documents. Every name is on a reserved test domain and every
/// address is a loopback or documentation address; nothing here was copied from a real machine.
/// </summary>
internal static class TailscaleFixtures
{
    public const string Host = "pc.example.test";

    /// <summary>Nothing is configured.</summary>
    public const string NoHandlers = "{}";

    /// <summary>Tailnet-only handlers of other programs on 443, at several paths and of every kind. Funnel is off.</summary>
    public const string TailnetOnly443 = """
        {
          "TCP": {
            "443": {
              "HTTPS": true
            }
          },
          "Web": {
            "pc.example.test:443": {
              "Handlers": {
                "/": {
                  "Proxy": "http://127.0.0.1:3000"
                },
                "/api/": {
                  "Proxy": "http://localhost:8080"
                },
                "/docs": {
                  "Path": "C:\\Sites\\docs"
                },
                "/hello": {
                  "Text": "hello"
                },
                "/old": {
                  "Redirect": "https://pc.example.test/new"
                }
              }
            }
          }
        }
        """;

    /// <summary>A port published through Funnel, with a root handler and a path mount of other programs.</summary>
    public const string FunnelRootAndMount443 = """
        {
          "TCP": {
            "443": {
              "HTTPS": true
            }
          },
          "Web": {
            "pc.example.test:443": {
              "Handlers": {
                "/": {
                  "Proxy": "http://127.0.0.1:8096"
                },
                "/files": {
                  "Proxy": "http://127.0.0.1:5000"
                }
              }
            }
          },
          "AllowFunnel": {
            "pc.example.test:443": true
          }
        }
        """;

    /// <summary>
    /// What the owner's PC may look like: two programs of others on 443 (published) and one on 8443
    /// (tailnet only), raw TCP on 5432, and a handler on 10000 that already points at the relay port.
    /// </summary>
    public const string SharedPortsAndTheRelay = """
        {
          "TCP": {
            "443": {
              "HTTPS": true
            },
            "5432": {
              "TCPForward": "127.0.0.1:5432"
            },
            "8443": {
              "HTTPS": true
            },
            "10000": {
              "HTTPS": true
            }
          },
          "Web": {
            "pc.example.test:443": {
              "Handlers": {
                "/": {
                  "Proxy": "http://127.0.0.1:8096"
                },
                "/files": {
                  "Proxy": "http://127.0.0.1:5000"
                }
              }
            },
            "pc.example.test:8443": {
              "Handlers": {
                "/": {
                  "Proxy": "http://127.0.0.1:9000"
                }
              }
            },
            "pc.example.test:10000": {
              "Handlers": {
                "/livecue": {
                  "Proxy": "http://127.0.0.1:47841"
                }
              }
            }
          },
          "AllowFunnel": {
            "pc.example.test:10000": true,
            "pc.example.test:443": true
          }
        }
        """;

    /// <summary><c>tailscale status --json</c> of a connected PC, with the members the reader must skip.</summary>
    public const string StatusRunning = """
        {
          "Version": "1.80.0-example",
          "TUN": true,
          "BackendState": "Running",
          "HaveNodeKey": true,
          "AuthURL": "",
          "TailscaleIPs": ["192.0.2.10"],
          "Self": {
            "ID": "example-node",
            "HostName": "PC",
            "DNSName": "PC.Example.Test.",
            "OS": "windows",
            "UserID": 1,
            "TailscaleIPs": ["192.0.2.10"],
            "Online": true,
            "ExitNode": false
          },
          "Health": [],
          "MagicDNSSuffix": "example.test",
          "CurrentTailnet": {
            "Name": "owner@example.com",
            "MagicDNSSuffix": "example.test",
            "MagicDNSEnabled": true
          },
          "CertDomains": ["pc.example.test"],
          "Peer": {
            "nodekey:example": {
              "HostName": "phone",
              "DNSName": "phone.example.test.",
              "Online": false
            }
          },
          "User": {
            "1": {
              "ID": 1,
              "LoginName": "owner@example.com",
              "DisplayName": "Owner"
            }
          }
        }
        """;

    /// <summary>Documents that are not the serve status the reader expects. Each must be "unparseable".</summary>
    public static readonly IReadOnlyList<string> Malformed =
    [
        string.Empty,
        "   ",
        "not json",
        "null",
        "[]",
        "42",
        "\"{}\"",
        "{",
        "{\"TCP\": {\"443\": {\"HTTPS\": true}}",
        "{} trailing",
        "{/* a comment */}",
        "{\"TCP\": {\"443\": {\"HTTPS\": true},}}",
        "{\"TCP\": []}",
        "{\"TCP\": null}",
        "{\"TCP\": {\"https\": {\"HTTPS\": true}}}",
        "{\"TCP\": {\"0\": {\"HTTPS\": true}}}",
        "{\"TCP\": {\"0443\": {\"HTTPS\": true}}}",
        "{\"TCP\": {\"70000\": {\"HTTPS\": true}}}",
        "{\"TCP\": {\"-443\": {\"HTTPS\": true}}}",
        "{\"TCP\": {\" 443\": {\"HTTPS\": true}}}",
        "{\"TCP\": {\"443\": true}}",
        "{\"Web\": \"pc.example.test:443\"}",
        "{\"Web\": {\"pc.example.test\": {\"Handlers\": {}}}}",
        "{\"Web\": {\"pc.example.test:https\": {\"Handlers\": {}}}}",
        "{\"Web\": {\":443\": {\"Handlers\": {}}}}",
        "{\"Web\": {\"pc.example.test:443\": []}}",
        "{\"Web\": {\"pc.example.test:443\": {\"Handlers\": []}}}",
        "{\"Web\": {\"pc.example.test:443\": {\"Handlers\": {\"/\": \"http://127.0.0.1:47841\"}}}}",
        "{\"Web\": {\"pc.example.test:443\": {\"Handlers\": {\"/\": {\"Proxy\": 47841}}}}}",
        "{\"Web\": {\"pc.example.test:443\": {\"Handlers\": {\"/\": {\"Proxy\": null}}}}}",
        "{\"AllowFunnel\": [\"pc.example.test:443\"]}",
        "{\"AllowFunnel\": {\"pc.example.test:443\": \"true\"}}",
        "{\"AllowFunnel\": {\"pc.example.test:443\": 1}}",
        "{\"AllowFunnel\": {\"pc.example.test\": true}}",
        "{\"Foreground\": []}",
        "{\"Foreground\": {\"session-1\": null}}",
        "{\"Foreground\": {\"session-1\": {\"TCP\": []}}}",
        "{\"Services\": \"svc:example\"}",
        "{\"Services\": {\"svc:example\": {\"Web\": {\"example\": {}}}}}",

        // A member named twice: two readers could disagree about which one counts.
        "{\"AllowFunnel\": {\"pc.example.test:443\": false, \"pc.example.test:443\": true}}",
        "{\"TCP\": {}, \"TCP\": {\"443\": {\"HTTPS\": true}}}",
        "{\"Other\": {\"a\": 1, \"a\": 2}}",

        // Deeper than any serve configuration is.
        string.Concat(Enumerable.Repeat("{\"a\":", 40)) + "1" + new string('}', 40),
    ];

    private static readonly DateTimeOffset Created = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    public static string Target(int relayPort = 47841) =>
        "http://127.0.0.1:" + relayPort.ToString(CultureInfo.InvariantCulture);

    public static OwnedMapping PrivateEntry(int httpsPort = 11443, int relayPort = 47841) =>
        new(LiveCue.Relay.ConnectionMode.Private, httpsPort, "/", Target(relayPort), Created);

    public static OwnedMapping PublicEntry(int httpsPort = 10000, string prefix = "/livecue", int relayPort = 47841) =>
        new(LiveCue.Relay.ConnectionMode.Public, httpsPort, prefix, Target(relayPort), Created);
}

/// <summary>
/// Builds a serve status document the way Tailscale lays one out: a web handler brings the TCP entry of
/// its port with it, and Funnel is one entry per "host:port". Used where a test needs a scenario that
/// differs from another in one detail.
/// </summary>
internal sealed class ServeDoc
{
    private readonly JsonObject _root = new();

    public static ServeDoc New() => new();

    /// <summary>A proxy handler to <c>http://127.0.0.1:&lt;targetPort&gt;</c>.</summary>
    public ServeDoc Proxy(int port, string mount, int targetPort = 47841, string host = TailscaleFixtures.Host) =>
        Handler(port, mount, "Proxy", TailscaleFixtures.Target(targetPort), host);

    public ServeDoc Handler(int port, string mount, string kind, string target, string host = TailscaleFixtures.Host)
    {
        Child(_root, "TCP")[Text(port)] ??= new JsonObject { ["HTTPS"] = true };
        Child(Child(Child(_root, "Web"), host + ":" + Text(port)), "Handlers")[mount] = new JsonObject { [kind] = target };
        return this;
    }

    public ServeDoc Funnel(int port, bool on = true, string host = TailscaleFixtures.Host)
    {
        Child(_root, "AllowFunnel")[host + ":" + Text(port)] = on;
        return this;
    }

    public ServeDoc TcpForward(int port, string to = "127.0.0.1:5432")
    {
        Child(_root, "TCP")[Text(port)] = new JsonObject { ["TCPForward"] = to };
        return this;
    }

    /// <summary>A foreground session with a configuration of its own.</summary>
    public ServeDoc Foreground(string session, ServeDoc config)
    {
        Child(_root, "Foreground")[session] = config._root.DeepClone();
        return this;
    }

    /// <summary>A service with a configuration of its own.</summary>
    public ServeDoc Service(string name, ServeDoc config)
    {
        Child(_root, "Services")[name] = config._root.DeepClone();
        return this;
    }

    /// <summary>Any other member, as a later Tailscale might add one.</summary>
    public ServeDoc Member(string name, JsonNode? value)
    {
        _root[name] = value;
        return this;
    }

    public string Json() => _root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public override string ToString() => Json();

    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is not JsonObject child)
        {
            child = new JsonObject();
            parent[name] = child;
        }

        return child;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
