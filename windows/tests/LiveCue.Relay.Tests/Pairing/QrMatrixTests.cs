using System.Text;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Tests.Support;
using Net.Codecrete.QrCodeGenerator;

namespace LiveCue.Relay.Tests.Pairing;

/// <summary>
/// Protocol P2-144: the matrix handed to the desktop is the byte-mode, level M encoding of the exact
/// payload text. No decoder is used: the matrix is compared, module for module, with an encoding the
/// test makes itself from the text it expects, and its fixed parts are read straight from the modules.
/// </summary>
public sealed class QrMatrixTests
{
    // Data codewords of versions 1 to 20 at level M (ISO/IEC 18004, table 7).
    private static readonly int[] DataCodewordsAtM =
        [16, 28, 44, 64, 86, 108, 124, 154, 182, 216, 254, 290, 334, 365, 415, 453, 507, 563, 627, 669];

    [Theory]
    [InlineData("https://a/")]
    [InlineData("https://pc.example.test:11443/")]
    [InlineData("https://pc.example.test:10000/livecue/")]
    [InlineData("https://a-rather-long-machine-name.a-tailnet-name.example.test:10000/first-segment/second.segment/third_segment/fourth~segment/")]
    public async Task The_matrix_is_square_at_least_21_and_is_the_byte_mode_level_M_encoding_of_the_payload(string endpoint)
    {
        await using var host = await RelayTestHost.StartAsync(options => options.Endpoint = endpoint);
        var expectedText = "{\"endpoint\":\"" + endpoint + "\",\"token\":\"" + host.Token + "\"}";

        var qr = await host.Relay.ShowPairingAsync(CancellationToken.None);
        var modules = qr.Modules;

        // Square, at least 21, and exactly the size of the smallest version that holds the text at level M.
        var size = modules.Length;
        Assert.True(size >= 21);
        Assert.All(modules, row => Assert.Equal(size, row.Length));
        var version = SmallestVersionAtM(Encoding.UTF8.GetByteCount(expectedText));
        Assert.Equal((version * 4) + 17, size);

        // The independent encoding: one byte-mode segment, level M, not raised.
        var expected = QrCode.EncodeSegments(
            [DataSegment.MakeSegment(DataSegmentMode.Binary, new ArraySegment<byte>(Encoding.UTF8.GetBytes(expectedText)))],
            QrCode.Ecc.Medium,
            1,
            40,
            boostEcl: false);
        Assert.Equal(QrCode.Ecc.Medium, expected.ErrorCorrectionLevel);
        Assert.Equal(version, expected.Version);
        Assert.Equal(expected.Size, size);
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                Assert.True(
                    expected.GetModule(column, row) == modules[row][column],
                    $"Module at row {row}, column {column} differs.");
            }
        }

        // Read from the modules themselves: the three finder patterns, the timing lines and the level.
        AssertFinder(modules, top: 0, left: 0);
        AssertFinder(modules, top: 0, left: size - 7);
        AssertFinder(modules, top: size - 7, left: 0);
        for (var index = 8; index < size - 8; index++)
        {
            Assert.Equal(index % 2 == 0, modules[6][index]);
            Assert.Equal(index % 2 == 0, modules[index][6]);
        }

        Assert.True(modules[size - 8][8], "The module that is always dark is light.");
        Assert.Equal(0, ErrorCorrectionBits(modules));

        // The matrix is the whole of it: no quiet zone was added.
        Assert.True(modules[0][0] && modules[0][size - 1] && modules[size - 1][0]);
    }

    [Fact]
    public void Another_text_gives_another_matrix_and_the_same_text_the_same()
    {
        var first = QrMatrix.Encode(Payload(new string('A', 43)));
        var again = QrMatrix.Encode(Payload(new string('A', 43)));
        var other = QrMatrix.Encode(Payload(new string('B', 43)));

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    private static byte[] Payload(string token) =>
        PairingPayload.Utf8(RelayTestHost.Endpoint, Encoding.ASCII.GetBytes(token));

    /// <summary>
    /// The smallest version whose data codewords at level M hold a byte-mode segment of that length:
    /// four bits of mode, the character count (8 bits up to version 9, 16 from version 10), the bytes.
    /// </summary>
    private static int SmallestVersionAtM(int byteCount)
    {
        for (var version = 1; version <= DataCodewordsAtM.Length; version++)
        {
            var countBits = version <= 9 ? 8 : 16;
            if (4 + countBits + (byteCount * 8) <= DataCodewordsAtM[version - 1] * 8)
            {
                return version;
            }
        }

        throw new InvalidOperationException("The test's table ends before this length.");
    }

    private static void AssertFinder(bool[][] modules, int top, int left)
    {
        for (var row = 0; row < 7; row++)
        {
            for (var column = 0; column < 7; column++)
            {
                var ring = row is 0 or 6 || column is 0 or 6;
                var core = row is >= 2 and <= 4 && column is >= 2 and <= 4;
                Assert.True(
                    (ring || core) == modules[top + row][left + column],
                    $"Finder pattern at row {top}, column {left} is wrong at ({row}, {column}).");
            }
        }
    }

    /// <summary>
    /// The two error-correction bits of the format information beside the top-left finder pattern, after
    /// the fixed mask 101010000010010 is removed. Level M is 00. The fifteen bits must also be a valid
    /// code word, or what was read was not format information.
    /// </summary>
    private static int ErrorCorrectionBits(bool[][] modules)
    {
        // Bit i of the format information, as (column, row).
        (int Column, int Row)[] positions =
        [
            (8, 0), (8, 1), (8, 2), (8, 3), (8, 4), (8, 5), (8, 7), (8, 8),
            (7, 8), (5, 8), (4, 8), (3, 8), (2, 8), (1, 8), (0, 8),
        ];
        var bits = 0;
        for (var index = 0; index < positions.Length; index++)
        {
            if (modules[positions[index].Row][positions[index].Column])
            {
                bits |= 1 << index;
            }
        }

        bits ^= 0x5412;
        var data = bits >> 10;
        var remainder = data;
        for (var round = 0; round < 10; round++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }

        Assert.Equal((data << 10) | remainder, bits);
        return data >> 3;
    }
}
