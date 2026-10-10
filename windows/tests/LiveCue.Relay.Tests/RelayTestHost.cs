using System.Collections.Concurrent;
using LiveCue.Relay.Authentication;

namespace LiveCue.Relay.Tests;

/// <summary>
/// A real relay on a real Kestrel listener, on a port the system picks (port 0), with an HTTP
/// client pointed at it. Every test gets its own.
/// </summary>
internal sealed class RelayTestHost : IAsyncDisposable
{
    public const string Token = "relay-test-token";

    private readonly ConcurrentQueue<RelayEvent> _events = new();

    private RelayTestHost(RelayHost relay, TimeProvider timeProvider)
    {
        Relay = relay;
        TimeProvider = timeProvider;
        Relay.EventRaised += (_, relayEvent) => _events.Enqueue(relayEvent);
    }

    public RelayHost Relay { get; }

    public TimeProvider TimeProvider { get; }

    public HttpClient Client { get; private set; } = null!;

    public int Port { get; private set; }

    public IReadOnlyList<RelayEvent> Events => [.. _events];

    public static async Task<RelayTestHost> StartAsync(
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;
        var host = new RelayTestHost(new RelayHost(timeProvider), timeProvider);
        await host.Relay.StartAsync(Options(), cancellationToken);
        host.Port = host.Relay.Port ?? throw new InvalidOperationException("The relay reported no port.");
        host.Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{host.Port}") };
        return host;
    }

    public static RelayOptions Options(int port = 0) =>
        new() { Authenticator = new SharedTokenAuthenticator(Token), Port = port };

    public static HttpRequestMessage Request(HttpMethod method, string path, string? authorization = "Bearer " + Token)
    {
        var request = new HttpRequestMessage(method, path);
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await Relay.DisposeAsync();
    }
}
