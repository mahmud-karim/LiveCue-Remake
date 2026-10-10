using System.Text;
using LiveCue.Relay.Host;
using Net.Codecrete.QrCodeGenerator;

namespace LiveCue.Hosting.Tests.ConsoleHost;

/// <summary>
/// The terminal QR of <c>--show-pairing</c> (W-design 4.16): two module rows per character row with
/// half-block characters, and a quiet zone of four modules. The token below is synthetic.
/// </summary>
public sealed class TerminalQrTests
{
    private static readonly string Payload =
        "{\"endpoint\":\"https://pc.example.test:11443/\",\"token\":\"" + new string('A', 43) + "\"}";

    [Theory]
    [InlineData("https://a/")]
    [InlineData("https://pc.example.test:11443/")]
    [InlineData("https://a-rather-long-machine-name.a-tailnet-name.example.test:10000/first-segment/second.segment/")]
    public void The_matrix_is_the_byte_mode_level_M_code_of_the_payload(string endpoint)
    {
        var payload = "{\"endpoint\":\"" + endpoint + "\",\"token\":\"" + new string('A', 43) + "\"}";

        var modules = TerminalQr.Encode(payload);

        // The same parameters as the code the desktop shows (protocol 4.2): one byte-mode segment, level
        // M, the smallest version, the level not raised.
        var expected = QrCode.EncodeSegments(
            [DataSegment.MakeSegment(DataSegmentMode.Binary, new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)))],
            QrCode.Ecc.Medium,
            1,
            40,
            boostEcl: false);
        Assert.Equal(QrCode.Ecc.Medium, expected.ErrorCorrectionLevel);
        Assert.Equal(expected.Size, modules.Length);
        for (var row = 0; row < modules.Length; row++)
        {
            Assert.Equal(expected.Size, modules[row].Length);
            for (var column = 0; column < modules.Length; column++)
            {
                Assert.True(expected.GetModule(column, row) == modules[row][column], $"Module at row {row}, column {column} differs.");
            }
        }
    }

    [Fact]
    public void The_lines_draw_two_module_rows_each_with_a_quiet_zone_of_four()
    {
        var modules = TerminalQr.Encode(Payload);
        var size = modules.Length;

        var lines = TerminalQr.Lines(Payload);

        // One character per module column and two module rows per line, the quiet zone on every side.
        Assert.Equal(4, TerminalQr.QuietZone);
        Assert.Equal((size + 8 + 1) / 2, lines.Count);
        Assert.All(lines, line => Assert.Equal(size + 8, line.Length));
        Assert.All(lines, line => Assert.Matches("^[█▀▄ ]+$", line));

        // Read back, the drawing is the matrix: a written half is a light module, an empty half a dark one.
        for (var row = -4; row < size + 4; row++)
        {
            for (var column = -4; column < size + 4; column++)
            {
                var character = lines[(row + 4) / 2][column + 4];
                var upperHalf = (row + 4) % 2 == 0;
                var light = character == '█' || character == (upperHalf ? '▀' : '▄');
                var inside = row >= 0 && row < size && column >= 0 && column < size;
                Assert.True(light == !(inside && modules[row][column]), $"Row {row}, column {column} is drawn wrongly.");
            }
        }

        // The quiet zone: two whole lines above, four whole columns on each side, and light below.
        Assert.Equal(new string('█', size + 8), lines[0]);
        Assert.Equal(new string('█', size + 8), lines[1]);
        Assert.All(lines, line => Assert.StartsWith("████", line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.EndsWith("████", line, StringComparison.Ordinal));
        Assert.Equal(new string('█', size + 8), lines[^1]);

        // The top left finder pattern starts right after the quiet zone: seven dark modules.
        Assert.Equal("████       █", lines[2][..12].Replace('▄', ' '));
    }

    [Fact]
    public void A_small_matrix_is_drawn_as_expected()
    {
        // Three by three: a dark diagonal. With the quiet zone it is eleven by eleven, drawn in six lines.
        bool[][] modules =
        [
            [true, false, false],
            [false, true, false],
            [false, false, true],
        ];
        const string full = "███████████";

        Assert.Equal(
            [
                full,
                full,
                "████▄▀█████",
                "██████▄████",
                full,
                full,
            ],
            TerminalQr.Lines(modules));
    }
}
