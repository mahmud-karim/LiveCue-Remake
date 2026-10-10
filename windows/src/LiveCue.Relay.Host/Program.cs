using System.Globalization;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

// LiveCueRelayHost.exe: the relay supervisor without a window (architecture 3.14). It is for headless
// use and for manual checks, not the everyday entry point.
//
//   LiveCueRelayHost [--check-only] [--port <n>] [--data-dir <path>] [--endpoint <url>] [--mode private|public]
//
// Exit codes: 0 clean stop, 2 prerequisite or usage failure, 3 relay start fault.
//
// The spike has the arguments above. --show-pairing, --events json, --codex and --lab arrive with the
// features they belong to. The pairing token is never printed and never read from the command line.

const int ExitClean = 0;
const int ExitPrerequisite = 2;
const int ExitStartFault = 3;
const string Usage =
    "Usage: LiveCueRelayHost [--check-only] [--port <n>] [--data-dir <path>] [--endpoint <url>] [--mode private|public]";

var checkOnly = false;
string? dataDirectoryArgument = null;
var overrides = new SupervisorOverrides();

for (var index = 0; index < args.Length; index++)
{
    var hasValue = index + 1 < args.Length;
    switch (args[index])
    {
        case "--check-only":
            checkOnly = true;
            break;

        case "--port" when hasValue
            && int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && (port == 0 || ProtocolConstants.IsAllowedRelayPort(port)):
            overrides = overrides with { Port = port };
            index++;
            break;

        case "--data-dir" when hasValue:
            dataDirectoryArgument = args[index + 1];
            index++;
            break;

        case "--endpoint" when hasValue && EndpointGrammar.TryCanonicalize(args[index + 1], out var endpoint):
            overrides = overrides with { Endpoint = endpoint };
            index++;
            break;

        case "--mode" when hasValue && args[index + 1] is "private" or "public":
            overrides = overrides with { Mode = args[index + 1] == "public" ? ConnectionMode.Public : ConnectionMode.Private };
            index++;
            break;

        default:
            Console.Error.WriteLine(Usage);
            return ExitPrerequisite;
    }
}

// Check 1 of architecture 3.11: the data folder is usable and settings.json loads, or defaults apply.
var dataDirectory = DataFolder.Resolve(dataDirectoryArgument);
var settings = new FileSettingsStore(dataDirectory);
var loaded = settings.Load();
if (loaded.Source == SettingsSource.DefaultsBecauseFileIsInvalid)
{
    Console.WriteLine("settings.json is not valid and was left untouched. Running on the default settings:");
    foreach (var problem in loaded.Problems)
    {
        Console.WriteLine("  " + problem);
    }
}

if (checkOnly)
{
    Console.WriteLine("LiveCue Relay prerequisites passed.");
    return ExitClean;
}

using var stopRequested = new CancellationTokenSource();
Console.CancelKeyPress += (_, cancel) =>
{
    cancel.Cancel = true;
    stopRequested.Cancel();
};

await using var supervisor = new RelaySupervisor(settings, dataDirectory, overrides: overrides);
supervisor.EventReceived += (_, relayEvent) => Console.WriteLine(relayEvent switch
{
    RelayStarted started => string.Create(
        CultureInfo.InvariantCulture,
        $"Relay {started.RelayVersion} started on 127.0.0.1:{started.Port} (protocol {started.ProtocolVersion}). PC address: {started.Endpoint}"),
    RelayStopped => "Relay stopped.",
    RelayFaulted faulted => faulted.Message,
    PhoneSeen => "iPhone seen.",
    Diagnostic diagnostic => "Relay diagnostic: " + diagnostic.Message,
    _ => relayEvent.GetType().Name,
});

try
{
    await supervisor.StartAsync(stopRequested.Token);
}
catch (RelayStartException)
{
    // The fault text was printed by the event handler above.
    return ExitStartFault;
}
catch (OperationCanceledException)
{
    return ExitClean;
}

Console.WriteLine("Press Ctrl+C to stop.");
try
{
    await Task.Delay(Timeout.Infinite, stopRequested.Token);
}
catch (OperationCanceledException)
{
    // Ctrl+C: fall through and stop.
}

await supervisor.StopAsync();
return ExitClean;
