using Net.Codecrete.QrCodeGenerator;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The module matrix of a pairing payload (protocol 4.2): square, row-major, true = dark, no quiet zone.
/// Whoever draws it adds the quiet zone of at least four modules.
/// </summary>
internal static class QrMatrix
{
    /// <summary>The smallest and the largest version of a QR code.</summary>
    private const int MinVersion = 1;
    private const int MaxVersion = 40;

    /// <summary>
    /// Encodes the payload's bytes as one byte-mode segment at error correction level M, in the smallest
    /// version that holds them, with the mask chosen by the encoder. The level is fixed: the encoder is
    /// told not to raise it when space allows. The payload array is only read.
    /// </summary>
    public static bool[][] Encode(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var segments = new List<DataSegment>(1)
        {
            DataSegment.MakeSegment(DataSegmentMode.Binary, new ArraySegment<byte>(payload)),
        };
        var code = QrCode.EncodeSegments(segments, QrCode.Ecc.Medium, MinVersion, MaxVersion, boostEcl: false);

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
}
