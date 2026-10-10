namespace LiveCue.Relay.Pairing;

/// <summary>
/// The module matrix of a pairing payload: square, row-major, true = dark, no quiet zone.
///
/// First version (foundation): <see cref="Placeholder"/> only, a fixed 21 x 21 pattern with the three
/// finder patterns and the timing lines of a version 1 code. It carries no data and does not scan.
/// Encoding the payload (byte mode, level M, <c>boostEcl: false</c>) is work package WP-W02.
/// </summary>
internal static class QrMatrix
{
    private const int Size = 21;

    public static bool[][] Placeholder()
    {
        var modules = new bool[Size][];
        for (var row = 0; row < Size; row++)
        {
            modules[row] = new bool[Size];
        }

        Finder(modules, 0, 0);
        Finder(modules, 0, Size - 7);
        Finder(modules, Size - 7, 0);
        for (var index = 8; index < Size - 8; index += 2)
        {
            modules[6][index] = true;
            modules[index][6] = true;
        }

        return modules;
    }

    private static void Finder(bool[][] modules, int top, int left)
    {
        for (var row = 0; row < 7; row++)
        {
            for (var column = 0; column < 7; column++)
            {
                var ring = row is 0 or 6 || column is 0 or 6;
                var core = row is >= 2 and <= 4 && column is >= 2 and <= 4;
                modules[top + row][left + column] = ring || core;
            }
        }
    }
}
