using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveCue.IconTool;

/// <summary>
/// <c>LiveCue.IconTool &lt;repository root&gt; [--frames &lt;folder&gt;]</c>
///
/// Draws the brand mark from the numbers of the design system (section 8.2) and writes the two icons
/// that are committed (W-design 5.14):
///
///   windows/src/LiveCue.Desktop/Assets/LiveCue.ico                      nine frames, each a 32-bit PNG entry
///   ios/App/Resources/Assets.xcassets/AppIcon.appiconset/AppIcon.png    1024 x 1024, opaque
///
/// With <c>--frames</c> it also writes every frame as a PNG of its own and a sheet that shows them on a
/// light and on a dark ground, the small ones enlarged, for a person to look at.
///
/// A developer tool, run by hand when the mark changes. A build never runs it and it is not published.
/// </summary>
internal static class Program
{
    private const int ExitDone = 0;
    private const int ExitUsage = 2;

    private const string FramesSwitch = "--frames";

    private static readonly string[] IcoPath = ["windows", "src", "LiveCue.Desktop", "Assets", "LiveCue.ico"];
    private static readonly string[] AppIconPath = ["ios", "App", "Resources", "Assets.xcassets", "AppIcon.appiconset", "AppIcon.png"];

    [STAThread]
    private static int Main(string[] args)
    {
        if (!TryReadArguments(args, out var root, out var framesFolder, out var problem))
        {
            Console.Error.WriteLine(problem);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage: LiveCue.IconTool <repository root> [--frames <folder>]");
            Console.Error.WriteLine("  <repository root>   the folder that holds windows/ and ios/");
            Console.Error.WriteLine("  --frames <folder>   also write each frame, the 1024 px picture and a sheet of all of them as PNG files");
            return ExitUsage;
        }

        var frames = MarkDrawing.IconSizes.Select(MarkDrawing.Frame).ToList();
        var master = MarkDrawing.Master();

        var ico = Path.Combine([root, .. IcoPath]);
        File.WriteAllBytes(ico, IcoWriter.Ico(frames));
        Console.WriteLine($"Wrote {Path.GetRelativePath(root, ico)}: frames {string.Join(", ", MarkDrawing.IconSizes)}");

        var appIcon = Path.Combine([root, .. AppIconPath]);
        File.WriteAllBytes(appIcon, IcoWriter.Png(master));
        Console.WriteLine($"Wrote {Path.GetRelativePath(root, appIcon)}: {master.PixelWidth} x {master.PixelHeight}, opaque");

        if (framesFolder is not null)
        {
            Directory.CreateDirectory(framesFolder);
            foreach (var frame in frames)
            {
                File.WriteAllBytes(Path.Combine(framesFolder, Invariant($"icon-{frame.PixelWidth}.png")), IcoWriter.Png(frame));
            }

            File.WriteAllBytes(Path.Combine(framesFolder, Invariant($"icon-{master.PixelWidth}.png")), IcoWriter.Png(master));
            File.WriteAllBytes(Path.Combine(framesFolder, "icon-sheet.png"), IcoWriter.Png(Sheet(frames)));
            Console.WriteLine($"Wrote {frames.Count + 2} pictures to {framesFolder}");
        }

        return ExitDone;
    }

    /// <summary>
    /// Reads the command line. The repository root must hold both folders an icon is written into, so
    /// that a wrong path writes nothing anywhere.
    /// </summary>
    private static bool TryReadArguments(string[] args, out string root, out string? framesFolder, out string problem)
    {
        root = string.Empty;
        framesFolder = null;
        problem = string.Empty;

        if (args.Length is not (1 or 3) || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            problem = "Give the repository root, and nothing else but --frames <folder>.";
            return false;
        }

        if (args.Length == 3)
        {
            if (args[1] != FramesSwitch || string.IsNullOrWhiteSpace(args[2]) || args[2].StartsWith("--", StringComparison.Ordinal))
            {
                problem = "The only switch is --frames <folder>.";
                return false;
            }

            framesFolder = Path.GetFullPath(args[2]);
        }

        if (string.IsNullOrWhiteSpace(args[0]) || !Directory.Exists(args[0]))
        {
            problem = "The repository root is not a folder that exists.";
            return false;
        }

        root = Path.GetFullPath(args[0]);
        foreach (var target in new[] { IcoPath, AppIconPath })
        {
            var folder = Path.GetDirectoryName(Path.Combine([root, .. target]))!;
            if (!Directory.Exists(folder))
            {
                problem = $"This is not the repository root: {Path.GetRelativePath(root, folder)} is missing.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Every frame at its own size, and the frames up to 48 pixels enlarged eight times without
    /// smoothing, once on a light ground and once on a dark one: how a taskbar of either kind shows them.
    /// </summary>
    private static RenderTargetBitmap Sheet(List<BitmapSource> frames)
    {
        const int gap = 16;
        const int enlarge = 8;
        var enlarged = frames.Where(frame => frame.PixelWidth <= 48).Reverse().ToList();
        var width = gap + Math.Max(
            frames.Sum(frame => frame.PixelWidth + gap),
            enlarged.Sum(frame => (frame.PixelWidth * enlarge) + gap));
        var band = gap + frames.Max(frame => frame.PixelHeight) + gap + (enlarged.Max(frame => frame.PixelHeight) * enlarge) + gap;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            Color[] grounds = [Color.FromRgb(0xF3, 0xF3, 0xF3), Color.FromRgb(0x1F, 0x1F, 0x1F)];
            for (var index = 0; index < grounds.Length; index++)
            {
                var top = index * band;
                context.DrawRectangle(new SolidColorBrush(grounds[index]), null, new Rect(0, top, width, band));

                var x = gap;
                foreach (var frame in frames)
                {
                    context.DrawImage(frame, new Rect(x, top + gap, frame.PixelWidth, frame.PixelHeight));
                    x += frame.PixelWidth + gap;
                }

                x = gap;
                var row = top + gap + frames.Max(frame => frame.PixelHeight) + gap;
                foreach (var frame in enlarged)
                {
                    var large = Enlarge(frame, enlarge);
                    context.DrawImage(large, new Rect(x, row, large.PixelWidth, large.PixelHeight));
                    x += large.PixelWidth + gap;
                }
            }
        }

        var sheet = new RenderTargetBitmap(width, band * 2, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(visual);
        sheet.Freeze();
        return sheet;
    }

    /// <summary>Every pixel of a 32-bit picture as a square of <paramref name="times"/> pixels, without smoothing.</summary>
    private static BitmapSource Enlarge(BitmapSource frame, int times)
    {
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        var source = new byte[width * height * 4];
        frame.CopyPixels(source, width * 4, 0);

        var large = new byte[source.Length * times * times];
        var stride = width * times * 4;
        for (var y = 0; y < height * times; y++)
        {
            for (var x = 0; x < width * times; x++)
            {
                Buffer.BlockCopy(source, (((y / times) * width) + (x / times)) * 4, large, (y * stride) + (x * 4), 4);
            }
        }

        var picture = BitmapSource.Create(width * times, height * times, 96, 96, frame.Format, null, large, stride);
        picture.Freeze();
        return picture;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
