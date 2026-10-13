using System.IO;
using System.Windows.Media.Imaging;

namespace LiveCue.IconTool;

/// <summary>
/// Writes a Windows icon file whose frames are PNG entries (W-design 5.14): the six-byte header, one
/// sixteen-byte directory entry per frame, then each frame's PNG file as it is.
/// </summary>
internal static class IcoWriter
{
    private const int HeaderLength = 6;
    private const int EntryLength = 16;

    /// <summary>Encodes a picture as a PNG file. The same picture always gives the same bytes.</summary>
    public static byte[] Png(BitmapSource picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The bytes of an icon file with these frames, in this order. Every frame is square, at most 256
    /// pixels wide, and has 32 bits per pixel.
    /// </summary>
    public static byte[] Ico(IReadOnlyList<BitmapSource> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count is 0 or > ushort.MaxValue)
        {
            throw new ArgumentException("An icon file holds at least one frame.", nameof(frames));
        }

        var images = new List<byte[]>(frames.Count);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // 1 = icon
        writer.Write((ushort)frames.Count);

        var offset = HeaderLength + (EntryLength * frames.Count);
        foreach (var frame in frames)
        {
            if (frame.PixelWidth != frame.PixelHeight || frame.PixelWidth is < 1 or > 256 || frame.Format.BitsPerPixel != 32)
            {
                throw new ArgumentException("A frame must be square, at most 256 pixels wide, with 32 bits per pixel.", nameof(frames));
            }

            var image = Png(frame);
            images.Add(image);

            // A width or height of 256 is written as 0: the field is one byte.
            writer.Write((byte)(frame.PixelWidth == 256 ? 0 : frame.PixelWidth));
            writer.Write((byte)(frame.PixelHeight == 256 ? 0 : frame.PixelHeight));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // colour planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write((uint)image.Length);
            writer.Write((uint)offset);
            offset += image.Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
