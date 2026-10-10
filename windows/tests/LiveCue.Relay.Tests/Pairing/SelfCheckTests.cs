using System.Net;
using System.Net.Sockets;
using System.Text;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// The self-check of protocol 4.5 (statement P2-147): each time a QR is shown the relay requests its own
/// advertised health URL once, with a nonce of its own, and tells the desktop one of three results. The
/// production client is tested against a loopback stub that records what arrived on the wire.
/// </summary>
public sealed class SelfCheckTests
{
    private const string Healthy = """{"ok":true,"protocol":2,"relayVersion":"1.0.0"}""";
    private const string Nonce = "AbCdEfGhIjKlMnOpQrStUv";

    [Fact]
    public async Task One_request_with_the_three_headers()
    {
        await using var stub = new HealthStub(Response(200, Healthy));

        var verified = await new SelfCheckClient().GetHealthAsync(
            stub.Url("/livecue/v1/health"), TestTokens.Example, Nonce, CancellationToken.None);

        // One GET of exactly that target, with the three headers of protocol 4.5 and nothing a browser
        // or a cookie jar would add (the relay itself refuses Origin and Sec-Fetch-Site).
        Assert.True(verified);
        var lines = Assert.Single(stub.Requests).Split("\r\n");
        Assert.Equal("GET /livecue/v1/health HTTP/1.1", lines[0]);
        var headers = lines.Skip(1).Select(line => line.Split(": ", 2)).ToDictionary(
            parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Bearer " + TestTokens.Example, headers["Authorization"]);
        Assert.Equal("2", headers["X-LiveCue-Protocol"]);
        Assert.Equal(Nonce, headers["X-LiveCue-Self-Check"]);
        Assert.Equal($"127.0.0.1:{stub.Port}", headers["Host"]);
        Assert.Equal(4, headers.Count);
        Assert.Equal(1, stub.Connections);
    }

    [Fact]
    public async Task A_show_makes_one_check_of_the_advertised_health_url_with_a_nonce_of_its_own()
    {
        using var harness = new PairingHarness(endpoint: "https://pc.example.test:10000/livecue/");
        harness.SelfCheck.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await harness.Service.LoadOrCreateAsync(CancellationToken.None);
        var token = await PairingHarness.TokenOfAsync(harness.Service);

        await harness.Service.ShowAsync(CancellationToken.None);
        await PairingHarness.UntilAsync(() => harness.SelfCheck.Calls.Count == 1);
        var call = harness.SelfCheck.Calls[0];

        // E + "v1/health", the accepted token, and 16 random bytes as base64url.
        Assert.Equal("https://pc.example.test:10000/livecue/v1/health", call.HealthUrl.OriginalString);
        Assert.Equal(token, call.Token);
        Assert.Matches(@"\A[A-Za-z0-9_-]{22}\z", call.Nonce);

        // While the check runs the edge can recognise its nonce, and only that text.
        Assert.True(harness.Service.IsSelfCheckNonce(call.Nonce));
        Assert.False(harness.Service.IsSelfCheckNonce(call.Nonce + "A"));
        Assert.False(harness.Service.IsSelfCheckNonce(call.Nonce.AsSpan(0, 21)));
        Assert.False(harness.Service.IsSelfCheckNonce(call.Nonce.ToUpperInvariant() + call.Nonce.ToLowerInvariant()));
        Assert.False(harness.Service.IsSelfCheckNonce(string.Empty));
        Assert.False(harness.Service.IsSelfCheckNonce(Nonce));
        Assert.Empty(harness.Events.OfType<SelfCheckCompleted>());

        harness.SelfCheck.Gate.SetResult();
        var completed = Assert.Single(await harness.Events.WaitForAsync<SelfCheckCompleted>());

        // One result per shown QR, and afterwards the nonce is gone.
        Assert.Equal(SelfCheckResult.Verified, completed.Result);
        Assert.False(harness.Service.IsSelfCheckNonce(call.Nonce));
        Assert.Single(harness.SelfCheck.Calls);
        Assert.Single(harness.Events.OfType<PairingShown>());

        // The next check has another nonce.
        await harness.Service.ShowAsync(CancellationToken.None);
        await harness.Events.WaitForAsync<SelfCheckCompleted>(count: 2);
        Assert.NotEqual(call.Nonce, harness.SelfCheck.Calls[1].Nonce);
    }

    // Everything but a 200 with ok true and protocol 2 is "not verified"; the QR is shown all the same.
    [Theory]
    [InlineData("refused connection")]
    [InlineData("redirect")]
    [InlineData("401")]
    [InlineData("timeout")]
    [InlineData("connection closed without an answer")]
    [InlineData("502 from the proxy")]
    [InlineData("200 that is not JSON")]
    [InlineData("200 with ok false")]
    [InlineData("200 with another protocol")]
    [InlineData("200 with the protocol as text")]
    [InlineData("200 with an array")]
    [InlineData("200 with an empty body")]
    [InlineData("200 with a body over the limit")]
    public async Task Refused_redirect_401_and_timeout_are_not_verified(string kind)
    {
        await using var stub = new HealthStub(kind switch
        {
            "redirect" => Response(302, string.Empty, "Location: /v1/health-elsewhere"),
            "401" => Response(401, """{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}"""),
            "502 from the proxy" => Response(502, "Bad Gateway"),
            "200 that is not JSON" => Response(200, "ok"),
            "200 with ok false" => Response(200, """{"ok":false,"protocol":2}"""),
            "200 with another protocol" => Response(200, """{"ok":true,"protocol":3}"""),
            "200 with the protocol as text" => Response(200, """{"ok":true,"protocol":"2"}"""),
            "200 with an array" => Response(200, "[" + Healthy + "]"),
            "200 with an empty body" => Response(200, string.Empty),
            "200 with a body over the limit" => Response(200, Padded(ProtocolConstants.MaxProbeBodyBytes + 1)),
            "connection closed without an answer" => string.Empty,
            _ => null,
        });
        var url = stub.Url("/v1/health");
        if (kind == "refused connection")
        {
            // Nothing listens there any more.
            await stub.DisposeAsync();
        }

        // The production client behind the service, so that the result is the desktop's event.
        using var harness = new PairingHarness();
        var client = new SelfCheckClient();
        harness.SelfCheck.Handler = (call, ct) => client.GetHealthAsync(url, call.Token, call.Nonce, ct);
        await harness.Service.LoadOrCreateAsync(CancellationToken.None);

        var qr = await harness.Service.ShowAsync(CancellationToken.None);
        if (kind == "timeout")
        {
            // The stub holds the request and never answers. Nothing happens until C-49 has passed on
            // the relay's clock; then the request is abandoned.
            await PairingHarness.UntilAsync(() => stub.Requests.Count == 1);
            harness.Clock.Advance(ProtocolConstants.SelfCheckTimeout - TimeSpan.FromMilliseconds(1));
            await Task.Delay(50);
            Assert.Empty(harness.Events.OfType<SelfCheckCompleted>());
            harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        var completed = Assert.Single(await harness.Events.WaitForAsync<SelfCheckCompleted>());

        Assert.Equal(SelfCheckResult.NotVerified, completed.Result);
        Assert.True(qr.Modules.Length >= 21);
        Assert.Single(harness.Events.OfType<PairingShown>());
        Assert.Empty(harness.Events.OfType<Diagnostic>());

        // One check is at most one request on one connection, also when the connection was closed
        // before any answer: the HTTP stack is not allowed to try again.
        Assert.True(stub.Requests.Count <= 1);
        Assert.True(stub.Connections <= 1);
    }

    [Fact]
    public async Task A_body_of_exactly_the_limit_is_read_and_a_caller_that_cancels_gets_the_cancellation()
    {
        await using var atLimit = new HealthStub(Response(200, Padded(ProtocolConstants.MaxProbeBodyBytes)));
        await using var silent = new HealthStub(null);
        var client = new SelfCheckClient();

        Assert.True(await client.GetHealthAsync(atLimit.Url("/v1/health"), TestTokens.Example, Nonce, CancellationToken.None));

        // Only cancellation by the caller throws; every other failure is "false".
        using var cancel = new CancellationTokenSource();
        var pending = client.GetHealthAsync(silent.Url("/v1/health"), TestTokens.Example, Nonce, cancel.Token);
        await PairingHarness.UntilAsync(() => silent.Requests.Count == 1);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    // Test plan S-36: the token goes to the advertised endpoint and nowhere else.
    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task A_redirect_is_not_followed(int status)
    {
        await using var elsewhere = new HealthStub(Response(200, Healthy));
        await using var advertised = new HealthStub(Response(status, string.Empty, "Location: " + elsewhere.Url("/v1/health")));

        var verified = await new SelfCheckClient().GetHealthAsync(
            advertised.Url("/v1/health"), TestTokens.Example, Nonce, CancellationToken.None);

        Assert.False(verified);
        Assert.Single(advertised.Requests);
        Assert.Equal(0, elsewhere.Connections);
        Assert.Empty(elsewhere.Requests);
    }

    // The whole loop except TLS: the production client, a real relay, the real edge.
    [Theory]
    [InlineData(ConnectionMode.Private, false, SelfCheckResult.Verified)]
    [InlineData(ConnectionMode.Public, false, SelfCheckResult.Verified)]
    [InlineData(ConnectionMode.Public, true, SelfCheckResult.VerifiedThroughFunnel)]
    [InlineData(ConnectionMode.Private, true, SelfCheckResult.NotVerified)]
    public async Task A_funnel_marked_arrival_is_verified_through_funnel(
        ConnectionMode mode, bool throughFunnel, SelfCheckResult expected)
    {
        await using var host = await RelayTestHost.StartAsync(options => options.Mode = mode);
        ArriveAtTheRelay(host, throughFunnel);

        await host.Relay.ShowPairingAsync(CancellationToken.None);
        var completed = await host.Events.WaitForAsync<SelfCheckCompleted>();

        // Private mode refuses a Funnel-marked request at the edge, its own self-check included.
        Assert.Equal(expected, completed.Result);
        Assert.Single(host.SelfCheck.Calls);
    }

    [Fact]
    public async Task No_presence_event_results()
    {
        await using var host = await RelayTestHost.StartAsync();
        ArriveAtTheRelay(host, throughFunnel: false);

        await host.Relay.ShowPairingAsync(CancellationToken.None);
        var completed = await host.Events.WaitForAsync<SelfCheckCompleted>();
        await host.Events.SettleAsync();

        // The request arrived, was answered 200, and is not a phone.
        Assert.Equal(SelfCheckResult.Verified, completed.Result);
        Assert.Empty(host.Events.OfType<PhoneSeen>());

        // The nonce was for that one check. The same request afterwards is a phone like any other.
        var nonce = Assert.Single(host.SelfCheck.Calls).Nonce;
        using var stale = host.Request(HttpMethod.Get, "/v1/health");
        stale.Headers.TryAddWithoutValidation("X-LiveCue-Self-Check", nonce);
        using var response = await host.Client.SendAsync(stale);
        await host.Events.WaitForAsync<PhoneSeen>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(host.Events.OfType<PhoneSeen>());
    }

    // W-07: at most four checks at once; a fifth takes the place of the oldest.
    [Fact]
    public async Task A_fifth_check_cancels_the_oldest_which_is_reported_not_verified()
    {
        using var harness = new PairingHarness();
        harness.SelfCheck.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await harness.Service.LoadOrCreateAsync(CancellationToken.None);

        for (var shown = 1; shown <= ProtocolConstants.MaxSelfChecks; shown++)
        {
            await harness.Service.ShowAsync(CancellationToken.None);
            await PairingHarness.UntilAsync(() => harness.SelfCheck.Calls.Count == shown);
        }

        Assert.Empty(harness.Events.OfType<SelfCheckCompleted>());

        await harness.Service.ShowAsync(CancellationToken.None);
        await PairingHarness.UntilAsync(() => harness.SelfCheck.Calls.Count == ProtocolConstants.MaxSelfChecks + 1);
        var first = Assert.Single(await harness.Events.WaitForAsync<SelfCheckCompleted>());
        var calls = harness.SelfCheck.Calls;

        Assert.Equal(SelfCheckResult.NotVerified, first.Result);
        Assert.False(harness.Service.IsSelfCheckNonce(calls[0].Nonce));
        Assert.All(calls.Skip(1), call => Assert.True(harness.Service.IsSelfCheckNonce(call.Nonce)));
        Assert.Equal(calls.Count, calls.Select(call => call.Nonce).Distinct().Count());

        harness.SelfCheck.Gate.SetResult();
        var all = await harness.Events.WaitForAsync<SelfCheckCompleted>(count: ProtocolConstants.MaxSelfChecks + 1);

        Assert.Equal(
            Enumerable.Repeat(SelfCheckResult.Verified, ProtocolConstants.MaxSelfChecks),
            all.Skip(1).Select(completed => completed.Result));
        Assert.All(calls, call => Assert.False(harness.Service.IsSelfCheckNonce(call.Nonce)));
        Assert.Equal(ProtocolConstants.MaxSelfChecks + 1, harness.Events.OfType<PairingShown>().Count);
    }

    // "Through Funnel" belongs to the check whose nonce the request carried, not to its neighbour.
    [Fact]
    public async Task With_two_checks_in_progress_an_arrival_counts_for_the_check_whose_nonce_it_carried()
    {
        using var harness = new PairingHarness();
        var release = new List<TaskCompletionSource>();
        harness.SelfCheck.Handler = async (call, ct) =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (release)
            {
                release.Add(gate);
            }

            await gate.Task.WaitAsync(ct);
            return true;
        };
        await harness.Service.LoadOrCreateAsync(CancellationToken.None);
        for (var shown = 1; shown <= 2; shown++)
        {
            await harness.Service.ShowAsync(CancellationToken.None);
            await PairingHarness.UntilAsync(() =>
            {
                lock (release)
                {
                    return release.Count == shown;
                }
            });
        }

        var calls = harness.SelfCheck.Calls;

        // What the edge does for each arriving request, on the request's own thread.
        Assert.True(harness.Service.IsSelfCheckNonce(calls[1].Nonce));
        harness.Service.NoteSelfCheckArrival(throughFunnel: true);
        Assert.True(harness.Service.IsSelfCheckNonce(calls[0].Nonce));
        harness.Service.NoteSelfCheckArrival(throughFunnel: false);

        release[0].SetResult();
        var first = Assert.Single(await harness.Events.WaitForAsync<SelfCheckCompleted>());
        release[1].SetResult();
        var both = await harness.Events.WaitForAsync<SelfCheckCompleted>(count: 2);

        Assert.Equal(SelfCheckResult.Verified, first.Result);
        Assert.Equal(SelfCheckResult.VerifiedThroughFunnel, both[1].Result);
    }

    [Fact]
    public async Task A_check_in_progress_when_the_relay_stops_ends_without_an_event()
    {
        using var harness = new PairingHarness();
        harness.SelfCheck.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await harness.Service.LoadOrCreateAsync(CancellationToken.None);
        await harness.Service.ShowAsync(CancellationToken.None);
        await PairingHarness.UntilAsync(() => harness.SelfCheck.Calls.Count == 1);
        var nonce = harness.SelfCheck.Calls[0].Nonce;

        harness.Access.Stop();
        await PairingHarness.UntilAsync(() => !harness.Service.IsSelfCheckNonce(nonce));
        await Task.Delay(50);

        // The relay's last event is RelayStopped; a check that was cut off adds nothing after it.
        Assert.Empty(harness.Events.OfType<SelfCheckCompleted>());
    }

    [Fact]
    public async Task A_client_that_fails_unexpectedly_is_not_verified_and_its_message_stays_private()
    {
        using var harness = new PairingHarness();
        harness.SelfCheck.Handler = (_, _) => throw new InvalidOperationException("PRIVATE DIAGNOSTIC");
        await harness.Service.LoadOrCreateAsync(CancellationToken.None);
        var token = await PairingHarness.TokenOfAsync(harness.Service);

        await harness.Service.ShowAsync(CancellationToken.None);
        var completed = Assert.Single(await harness.Events.WaitForAsync<SelfCheckCompleted>());

        Assert.Equal(SelfCheckResult.NotVerified, completed.Result);
        var diagnostic = Assert.Single(harness.Events.OfType<Diagnostic>());
        Assert.Equal("pairing", diagnostic.Source);
        Assert.Equal("The connection check failed unexpectedly. (InvalidOperationException)", diagnostic.Message);
        Assert.All(harness.Events.Snapshot(), relayEvent =>
        {
            Assert.DoesNotContain("PRIVATE DIAGNOSTIC", relayEvent.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(token, relayEvent.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(harness.SelfCheck.Calls[0].Nonce, relayEvent.ToString(), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Makes the self-check arrive at the relay under test as the edge proxy would deliver it, over
    /// loopback: through the production client, or by hand when the proxy's Funnel mark is wanted.
    /// </summary>
    private static void ArriveAtTheRelay(RelayTestHost host, bool throughFunnel)
    {
        var client = new SelfCheckClient();
        host.SelfCheck.Handler = async (call, ct) =>
        {
            var url = new Uri($"http://127.0.0.1:{host.Port}/v1/health");
            if (!throughFunnel)
            {
                return await client.GetHealthAsync(url, call.Token, call.Nonce, ct);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", TestTokens.Bearer(call.Token));
            request.Headers.TryAddWithoutValidation("X-LiveCue-Protocol", "2");
            request.Headers.TryAddWithoutValidation("X-LiveCue-Self-Check", call.Nonce);
            request.Headers.TryAddWithoutValidation("Tailscale-Funnel-Request", "?1");
            using var response = await host.Client.SendAsync(request, ct);
            return response.StatusCode == HttpStatusCode.OK;
        };
    }

    private static string Response(int status, string body, params string[] headers)
    {
        var text = new StringBuilder();
        text.Append("HTTP/1.1 ").Append(status).Append(" Status\r\n");
        text.Append("Content-Type: application/json\r\n");
        text.Append("Content-Length: ").Append(Encoding.UTF8.GetByteCount(body)).Append("\r\n");
        text.Append("Connection: close\r\n");
        foreach (var header in headers)
        {
            text.Append(header).Append("\r\n");
        }

        return text.Append("\r\n").Append(body).ToString();
    }

    /// <summary>A healthy answer of exactly that many bytes.</summary>
    private static string Padded(int length)
    {
        const string Head = "{\"ok\":true,\"protocol\":2,\"pad\":\"";
        const string Tail = "\"}";
        return Head + new string('x', length - Head.Length - Tail.Length) + Tail;
    }

    /// <summary>
    /// A listener on a loopback port the system picks. It records the head of every request and answers
    /// each with the same scripted bytes, then closes. An empty script closes without answering; null
    /// holds the connection open and never answers.
    /// </summary>
    private sealed class HealthStub : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Lock _gate = new();
        private readonly List<string> _requests = [];
        private readonly string? _response;
        private readonly Task _accepting;
        private int _connections;
        private bool _disposed;

        public HealthStub(string? response)
        {
            _response = response;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accepting = Task.Run(AcceptAsync);
        }

        public int Port { get; }

        public int Connections => Volatile.Read(ref _connections);

        /// <summary>The request heads received so far, without the blank line that ends them.</summary>
        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        public Uri Url(string target) => new($"http://127.0.0.1:{Port}{target}");

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _stop.CancelAsync();
            _listener.Stop();
            await _accepting;
            _stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            var connections = new List<Task>();
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _connections);
                    connections.Add(ServeAsync(client));
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or InvalidOperationException)
            {
                // The stub was stopped, perhaps before it accepted anything.
            }

            await Task.WhenAll(connections);
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var head = new StringBuilder();
                    var buffer = new byte[4096];
                    while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await stream.ReadAsync(buffer, _stop.Token);
                        if (read == 0)
                        {
                            return;
                        }

                        head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    lock (_gate)
                    {
                        _requests.Add(head.ToString().Split("\r\n\r\n")[0]);
                    }

                    if (_response is null)
                    {
                        await Task.Delay(Timeout.Infinite, _stop.Token);
                    }
                    else if (_response.Length > 0)
                    {
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(_response), _stop.Token);
                    }
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException)
                {
                    // The stub was stopped, or the client went away.
                }
            }
        }
    }
}
