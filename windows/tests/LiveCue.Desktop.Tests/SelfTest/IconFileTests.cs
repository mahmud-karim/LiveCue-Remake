using System.Buffers.Binary;
using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;

namespace LiveCue.Desktop.Tests.SelfTest;

/// <summary>
/// The committed icon, <c>Assets/LiveCue.ico</c>, as the app carries it (W-design 5.14; design system
/// 8.2). It is drawn by <c>LiveCue.IconTool</c> and committed; no build rewrites it. The test reads the
/// copy inside the app's assembly, which is the one the window shows.
/// </summary>
public sealed class IconFileTests
{
    private static readonly int[] FrameSizes = [256, 128, 64, 48, 40, 32, 24, 20, 16];

    [Fact]
    public void The_ico_has_the_nine_frames()
    {
        var ico = ReadIcon();

        // The header: reserved 0, type 1 (icon), nine entries.
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        Assert.Equal(FrameSizes.Length, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));

        var sizes = new List<int>();
        var end = 6 + (16 * FrameSizes.Length);
        for (var index = 0; index < FrameSizes.Length; index++)
        {
            var entry = ico.AsSpan(6 + (16 * index), 16);

            // A width of 256 does not fit the one byte it has, and is written as 0.
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            Assert.Equal(width, height);
            Assert.Equal(0, entry[2]);
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]));
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));

            // The frames follow the directory one after another, with nothing between or after them.
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            Assert.Equal(end, offset);
            end = offset + length;

            // Each frame is a PNG file of that size with eight bits for each of red, green, blue and alpha.
            var png = ico.AsSpan(offset, length);
            Assert.True(png[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), $"Frame {width} is not a PNG file.");
            Assert.Equal("IHDR", Encoding.ASCII.GetString(png.Slice(12, 4)));
            Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(png[16..]));
            Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(png[20..]));
            Assert.Equal(8, png[24]);
            Assert.Equal(6, png[25]);
            Assert.Equal("IEND", Encoding.ASCII.GetString(png.Slice(length - 8, 4)));

            sizes.Add(width);
        }

        Assert.Equal(ico.Length, end);
        Assert.Equal(FrameSizes, sizes);
    }

    [Fact]
    public void The_window_and_the_executable_use_that_icon()
    {
        // The window: a pack URI to the resource this test just read.
        var window = File.ReadAllText(Path.Combine(DesktopProjectFolder(), "Shell", "MainWindow.xaml"));
        Assert.Contains("Icon=\"pack://application:,,,/Assets/LiveCue.ico\"", window, StringComparison.Ordinal);

        // The executable: the same file as its Win32 icon, and the file itself is the committed one.
        var project = File.ReadAllText(Path.Combine(DesktopProjectFolder(), "LiveCue.Desktop.csproj"));
        Assert.Contains("<ApplicationIcon>Assets\\LiveCue.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\LiveCue.ico\" />", project, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(Path.Combine(DesktopProjectFolder(), "Assets", "LiveCue.ico")), ReadIcon());
    }

    /// <summary>The icon as it is embedded in the app's assembly.</summary>
    private static byte[] ReadIcon()
    {
        var assembly = typeof(App).Assembly;
        var resources = new ResourceManager(assembly.GetName().Name + ".g", assembly);
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        var entry = set.Cast<DictionaryEntry>().SingleOrDefault(candidate => (string)candidate.Key == "assets/livecue.ico");
        var stream = Assert.IsAssignableFrom<Stream>(entry.Value);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The desktop project's folder in the checkout the tests were built from.</summary>
    private static string DesktopProjectFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "src", "LiveCue.Desktop");
            if (File.Exists(Path.Combine(candidate, "LiveCue.Desktop.csproj")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The tests do not run from a checkout of the repository.");
    }
}
