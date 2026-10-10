using System.Security.Cryptography;
using System.Text;
using Net.Codecrete.QrCodeGenerator;

namespace LiveCue.Relay.Host;

/// <summary>
/// Draws a pairing payload as a QR code in a terminal (W-design 4.16): two module rows per character row
/// with half-block characters, one character per module column, and a quiet zone of four modules on
/// every side.
///
/// The code is the one the desktop shows for the same payload: its bytes as one byte-mode segment at
/// error correction level M, in the smallest version that holds them (protocol 4.2).
///
/// A terminal writes light characters on a dark background, and a QR code is dark modules on a light
/// background. So the light modules are what is written (block characters) and the dark modules are left
/// empty: on the usual dark terminal the code appears the right way round.
/// </summary>
internal static class TerminalQr
{
    /// <summary>The quiet zone, in modules, on each of the four sides.</summary>
    public const int QuietZone = 4;

    /// <summary>The smallest and the largest version of a QR code.</summary>
    private const int MinVersion = 1;
    private const int MaxVersion = 40;

    private const char BothLight = '█';
    private const char UpperLight = '▀';
    private const char LowerLight = '▄';
    private const char BothDark = ' ';

    /// <summary>The character rows of the code of <paramref name="payload"/>, quiet zone included.</summary>
    public static IReadOnlyList<string> Lines(string payload) => Lines(Encode(payload));

    /// <summary>The module matrix of a payload: square, row-major, true = dark, no quiet zone.</summary>
    public static bool[][] Encode(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // The bytes hold the pairing token; they are cleared as soon as the code exists.
        var bytes = Encoding.UTF8.GetBytes(payload);
        try
        {
            var code = QrCode.EncodeSegments(
                [DataSegment.MakeSegment(DataSegmentMode.Binary, new ArraySegment<byte>(bytes))],
                QrCode.Ecc.Medium,
                MinVersion,
                MaxVersion,
                boostEcl: false);

            var size = code.Size;
            var modules = new bool[size][];
            for (var row = 0; row < size; row++)
            {
                var line = new bool[size];
                for (var column = 0; column < size; column++)
                {
                    line[column] = code.GetModule(column, row);
                }

                modules[row] = line;
            }

            return modules;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>The character rows of a module matrix, quiet zone included.</summary>
    public static IReadOnlyList<string> Lines(bool[][] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var size = modules.Length;
        var side = size + (2 * QuietZone);
        var lines = new List<string>((side + 1) / 2);
        var line = new StringBuilder(side);
        for (var top = 0; top < side; top += 2)
        {
            line.Clear();
            for (var column = 0; column < side; column++)
            {
                var upperDark = IsDark(modules, top - QuietZone, column - QuietZone);
                var lowerDark = IsDark(modules, top + 1 - QuietZone, column - QuietZone);
                line.Append((upperDark, lowerDark) switch
                {
                    (false, false) => BothLight,
                    (false, true) => UpperLight,
                    (true, false) => LowerLight,
                    (true, true) => BothDark,
                });
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    /// <summary>Everything outside the matrix is light: the quiet zone, and half a row below it.</summary>
    private static bool IsDark(bool[][] modules, int row, int column) =>
        row >= 0 && row < modules.Length && column >= 0 && column < modules[row].Length && modules[row][column];
}
