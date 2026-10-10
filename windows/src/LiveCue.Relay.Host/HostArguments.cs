using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Pairing;

namespace LiveCue.Relay.Host;

/// <summary>
/// The command line of LiveCueRelayHost.exe (architecture 3.14). There is no reset-pairing argument, and
/// the pairing token is never read from the command line.
/// </summary>
/// <param name="CheckOnly"><c>--check-only</c>: run the checks, print the result, exit.</param>
/// <param name="ShowPairing"><c>--show-pairing</c>: print the stored pairing's payload once and exit. It
/// starts no relay and binds no port, so it works while LiveCue Desktop is running.</param>
/// <param name="JsonEvents"><c>--events json</c>: one JSON object per event instead of a text line.</param>
/// <param name="DataDirectory"><c>--data-dir</c>, or null for the data folder of architecture 3.5.</param>
/// <param name="Overrides"><c>--endpoint</c>, <c>--port</c>, <c>--codex</c>, <c>--lab</c>, <c>--mode</c>.</param>
internal sealed record HostArguments(
    bool CheckOnly,
    bool ShowPairing,
    bool JsonEvents,
    string? DataDirectory,
    RelayOverrides Overrides)
{
    public const string Usage =
        "Usage: LiveCueRelayHost [--check-only] [--show-pairing] [--events json] [--endpoint <url>] "
        + "[--port <n>] [--data-dir <path>] [--codex <path>] [--lab <folder>] [--mode private|public]";

    /// <summary>False for an unknown argument, a missing value or a value that is not accepted.</summary>
    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out HostArguments? parsed)
    {
        ArgumentNullException.ThrowIfNull(args);
        parsed = null;

        var checkOnly = false;
        var showPairing = false;
        var jsonEvents = false;
        string? dataDirectory = null;
        var overrides = new RelayOverrides();

        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (name is "--check-only")
            {
                checkOnly = true;
                continue;
            }

            if (name is "--show-pairing")
            {
                showPairing = true;
                continue;
            }

            // Every other argument takes one value.
            if (index + 1 >= args.Count)
            {
                return false;
            }

            var value = args[++index];
            switch (name)
            {
                case "--events" when value is "json":
                    jsonEvents = true;
                    break;

                case "--endpoint" when AdvertisedEndpoint.TryCanonicalize(value, out var endpoint):
                    overrides = overrides with { Endpoint = endpoint };
                    break;

                case "--port" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                    && port is 0 or (>= 1024 and <= 65535 and not RelaySettings.ForbiddenPort):
                    overrides = overrides with { Port = port };
                    break;

                case "--data-dir" when value.Length > 0:
                    dataDirectory = value;
                    break;

                case "--codex" when value.Length > 0:
                    overrides = overrides with { CodexPath = value };
                    break;

                case "--lab" when value.Length > 0:
                    overrides = overrides with { LabFolder = value };
                    break;

                case "--mode" when value is "private" or "public":
                    overrides = overrides with { Mode = value == "public" ? ConnectionMode.Public : ConnectionMode.Private };
                    break;

                default:
                    return false;
            }
        }

        parsed = new HostArguments(checkOnly, showPairing, jsonEvents, dataDirectory, overrides);
        return true;
    }
}
