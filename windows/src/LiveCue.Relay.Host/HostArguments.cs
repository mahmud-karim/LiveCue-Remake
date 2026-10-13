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
/// <param name="JsonEvents"><c>--events json</c>: one JSON object per line instead of a text line.</param>
/// <param name="IncludeContent"><c>--include-content</c>: with <c>--events json</c>, the lines also carry
/// conversation text (transcripts, instructions, request and reply bodies, speech text).</param>
/// <param name="DataDirectory"><c>--data-dir</c> as an absolute path, or null for the data folder of
/// architecture 3.5.</param>
/// <param name="Overrides"><c>--endpoint</c>, <c>--port</c>, <c>--codex</c>, <c>--lab</c>, <c>--mode</c>.</param>
internal sealed record HostArguments(
    bool CheckOnly,
    bool ShowPairing,
    bool JsonEvents,
    bool IncludeContent,
    string? DataDirectory,
    RelayOverrides Overrides)
{
    public const string Usage =
        "Usage: LiveCueRelayHost [--check-only] [--show-pairing] [--events json] [--include-content] "
        + "[--endpoint <url>] [--port <n>] [--data-dir <path>] [--codex <path>] [--lab <folder>] "
        + "[--mode private|public]";

    /// <summary>
    /// False for an unknown argument, a missing value or a value that is not accepted: an endpoint that
    /// fails the grammar of protocol 4.3, a port other than 0 or 1024 to 65535, port 47831, a mode other
    /// than the two words, a path that cannot be made absolute, or the previous LiveCue's data folder.
    /// The three paths are made absolute against the current folder.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out HostArguments? parsed)
    {
        ArgumentNullException.ThrowIfNull(args);
        parsed = null;

        var checkOnly = false;
        var showPairing = false;
        var jsonEvents = false;
        var includeContent = false;
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

            if (name is "--include-content")
            {
                includeContent = true;
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

                case "--data-dir" when TryAbsolutePath(value, out var folder) && !DataFolder.IsPreviousAppFolder(folder):
                    dataDirectory = folder;
                    break;

                case "--codex" when TryAbsolutePath(value, out var codex):
                    overrides = overrides with { CodexPath = codex };
                    break;

                case "--lab" when TryAbsolutePath(value, out var lab):
                    overrides = overrides with { LabFolder = lab };
                    break;

                case "--mode" when value is "private" or "public":
                    overrides = overrides with { Mode = value == "public" ? ConnectionMode.Public : ConnectionMode.Private };
                    break;

                default:
                    return false;
            }
        }

        parsed = new HostArguments(checkOnly, showPairing, jsonEvents, includeContent, dataDirectory, overrides);
        return true;
    }

    private static bool TryAbsolutePath(string value, [NotNullWhen(true)] out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            path = Path.GetFullPath(value);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }
}
