using System.Globalization;
using System.IO;

namespace LiveCue.Desktop;

/// <summary>
/// The command line of LiveCueRemake.exe. Normal use has no arguments; the only switches belong to the
/// self-test (architecture 3.13):
///
///   LiveCueRemake.exe --self-test [--screenshots &lt;folder&gt;] [--scale 1|1.5]
/// </summary>
public sealed record AppArguments
{
    public const string SelfTestSwitch = "--self-test";
    public const string ScreenshotsSwitch = "--screenshots";
    public const string ScaleSwitch = "--scale";

    /// <summary>Run the built-in self-test instead of the normal window, then exit.</summary>
    public bool IsSelfTest { get; init; }

    /// <summary>Self-test only: the folder that receives one PNG per scene and size, and the log.</summary>
    public string? ScreenshotDirectory { get; init; }

    /// <summary>Self-test only: pixels per layout unit in the PNGs, 1 or 1.5.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>Reads the switches above. Anything else on the command line is ignored.</summary>
    /// <exception cref="ArgumentException">A switch has no value, or a value it does not accept.</exception>
    public static AppArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parsed = new AppArguments();
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
}
