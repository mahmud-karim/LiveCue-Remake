using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// Off-screen rendering of a scene (W-design 5.13). The pictures come from WPF's own software renderer
/// (<see cref="RenderTargetBitmap"/>), which draws the same tree with the same brushes and effects but
/// with greyscale text. They are not a capture of the screen: WPF presents nothing for a window that is
/// off-screen, or while the display is asleep. So ClearType on the real window is protected by the
/// crisp-text rule, not by pixels.
///
/// With a folder, each scene is written twice: <c>&lt;scene&gt;.png</c> at the default window size and
/// <c>&lt;scene&gt;-min.png</c> at the minimum size, each at 96 DPI times the scale.
/// </summary>
internal sealed class SceneRenderer(string? directory, double scale)
{
    /// <summary>The window's default size (screens 1.1).</summary>
    public static readonly Size DefaultSize = new(1240, 660);

    /// <summary>The window's minimum size.</summary>
    public static readonly Size MinimumSize = new(960, 620);

    private readonly List<string> _written = [];

    /// <summary>True when the run was asked for screenshots.</summary>
    public bool WritesFiles => directory is not null;

    /// <summary>The files written so far, in order.</summary>
    public IReadOnlyList<string> Written => _written;

    /// <summary>The file name of a scene at one of the two sizes.</summary>
    public static string FileName(string scene, Size size) => size == MinimumSize ? scene + "-min.png" : scene + ".png";

    /// <summary>
    /// Draws <paramref name="root"/> as it is laid out now, at <paramref name="pixelsPerUnit"/> pixels
    /// per layout unit.
    /// </summary>
    public static RenderTargetBitmap Render(FrameworkElement root, double pixelsPerUnit = 1)
    {
        ArgumentNullException.ThrowIfNull(root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(root.ActualWidth * pixelsPerUnit),
            (int)Math.Round(root.ActualHeight * pixelsPerUnit),
            96 * pixelsPerUnit,
            96 * pixelsPerUnit,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Writes the scene as it is laid out now, at the run's scale, and returns the file name; null when
    /// no folder was given. <paramref name="frame"/> is the picture at one pixel per unit, when the
    /// caller already has it.
    /// </summary>
    public string? Save(FrameworkElement root, string scene, Size size, BitmapSource? frame = null)
    {
        if (directory is null)
        {
            return null;
        }

        var picture = Math.Abs(scale - 1) < 0.001 && frame is not null ? frame : Render(root, scale);
        var fileName = FileName(scene, size);
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using (var stream = File.Create(Path.Combine(directory, fileName)))
        {
            encoder.Save(stream);
        }

        _written.Add(fileName);
        return fileName;
    }
}
