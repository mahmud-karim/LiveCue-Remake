using System.Globalization;
using System.IO;
using System.Reflection;

namespace LiveCue.Desktop.Composition;

/// <summary>
/// The command line of LiveCueRemake.exe and what the app knows about its own installation. Normal use
/// has no arguments; the only switches belong to the self-test (architecture 3.13):
///
///   LiveCueRemake.exe --self-test [--screenshots &lt;folder&gt;] [--scale 1|1.5]
/// </summary>
public sealed record AppEnvironment
{
    public const string SelfTestSwitch = "--self-test";
    public const string ScreenshotsSwitch = "--screenshots";
    public const string ScaleSwitch = "--scale";

    /// <summary>The version of this executable ("1.0.0"), which the relay reports to phones.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The notices file the publish step puts beside the executable.</summary>
    public static string ThirdPartyNoticesPath => Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");

    /// <summary>Run the built-in self-test instead of the normal window, then exit.</summary>
    public bool IsSelfTest { get; init; }

    /// <summary>Self-test only: the folder that receives one PNG per scene and size, and the log.</summary>
    public string? ScreenshotDirectory { get; init; }

    /// <summary>Self-test only: pixels per layout unit in the PNGs, 1 or 1.5.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>Reads the switches above. Anything else on the command line is ignored.</summary>
    /// <exception cref="ArgumentException">A switch has no value, or a value it does not accept.</exception>
    public static AppEnvironment Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parsed = new AppEnvironment();
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case SelfTestSwitch:
                    parsed = parsed with { IsSelfTest = true };
                    break;

                case ScreenshotsSwitch:
                    parsed = parsed with { ScreenshotDirectory = Path.GetFullPath(ValueOf(args, ref index, "a folder path")) };
                    break;

                case ScaleSwitch:
                    var text = ValueOf(args, ref index, "1 or 1.5");
                    if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var scale)
                        || scale is not (1 or 1.5))
                    {
                        throw new ArgumentException($"{ScaleSwitch} must be 1 or 1.5.");
                    }

                    parsed = parsed with { Scale = scale };
                    break;

                default:
                    break;
            }
        }

        return parsed;
    }

    private static string ValueOf(IReadOnlyList<string> args, ref int index, string what)
    {
        var name = args[index];
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} needs {what} after it.");
        }

        index++;
        return args[index];
    }

    private static string ReadVersion()
    {
        var informational = typeof(AppEnvironment).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        // Drop build metadata ("1.0.0+abc123") if a build ever adds it.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
