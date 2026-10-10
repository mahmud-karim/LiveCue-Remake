using System.Globalization;
using LiveCue.Relay;
using LiveCue.Relay.Authentication;

// Console host for the relay, for headless runs.
//
//   LiveCue.Relay.Host [--port <n>]
//
// The pairing token is read from the LIVECUE_RELAY_TOKEN environment variable. It is never taken
// from the command line, because command lines are visible to every process on the PC.

const string TokenVariable = "LIVECUE_RELAY_TOKEN";
const int ExitOk = 0;
const int ExitStartFailed = 1;
const int ExitUsage = 2;

var port = RelayDefaults.Port;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--port"
        && index + 1 < args.Length
        && int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        && RelayOptions.IsAllowedPort(parsed))
    {
        port = parsed;
        index++;
        continue;
    }

    Console.Error.WriteLine("Usage: LiveCue.Relay.Host [--port <0 or 1024-65535>]");
    return ExitUsage;
}

var token = Environment.GetEnvironmentVariable(TokenVariable);
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine($"Set the {TokenVariable} environment variable to the pairing token before starting the relay.");
    return ExitUsage;
}

using var stopRequested = new CancellationTokenSource();
Console.CancelKeyPress += (_, cancel) =>
{
    cancel.Cancel = true;
    stopRequested.Cancel();
};

await using var relay = new RelayHost();
relay.EventRaised += (_, relayEvent) => Console.WriteLine(relayEvent switch
{
    RelayEvent.Started started =>
        $"LiveCue relay {RelayDefaults.RelayVersion} listening on http://127.0.0.1:{started.Port} (Ctrl+C to stop)",
    RelayEvent.Stopped => "LiveCue relay stopped",
    RelayEvent.PhoneSeen seen => $"Phone seen at {seen.At:O}",
    _ => relayEvent.ToString(),
});

try
{
    await relay.StartAsync(
        new RelayOptions { Authenticator = new SharedTokenAuthenticator(token), Port = port },
        stopRequested.Token);
}
catch (RelayStartException exception)
{
    Console.Error.WriteLine(exception.Message);
    return ExitStartFailed;
}
catch (OperationCanceledException)
{
    return ExitOk;
}

try
{
    await Task.Delay(Timeout.Infinite, stopRequested.Token);
}
catch (OperationCanceledException)
{
    // Ctrl+C: fall through and stop.
}

await relay.StopAsync();
return ExitOk;
