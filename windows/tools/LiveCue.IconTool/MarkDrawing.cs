using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace LiveCue.IconTool;

/// <summary>
/// The LiveCue mark, drawn from the numbers of the design system (docs/design/01-design-system.md,
/// section 8.2): a speech bubble holding three voice bars, a blue glass bubble on a pale Daylight tile.
///
/// The master artwork is a 1024 x 1024 canvas with its origin at the top left. Three ways of drawing
/// come from it, as the design system says: the master itself (the iPhone icon and the 256 and 128
/// frames), a flat version of it (64 to 32), and the glyph alone on the pixel grid (24 to 16). The
/// three groups of the artwork (tile, bubble, bars) are separate layers here, so a layered icon can be
/// made later without redrawing.
/// </summary>
internal static class MarkDrawing
{
    /// <summary>The side of the master artwork.</summary>
    public const int MasterSize = 1024;

    /// <summary>The corner radius of the Windows tile, as a part of its side.</summary>
    private const double TileCornerShare = 0.235;

    private static readonly Rect Canvas = new(0, 0, MasterSize, MasterSize);

    // The bubble: a rounded rectangle united with its tail.
    private static readonly Rect BubbleBody = new(232, 222, 560, 460);
    private const double BubbleRadius = 150;
    private const string BubbleTail = "M 330 640 L 296 800 C 292 818 306 826 320 816 L 508 680 Z";

    // The bars: three capsules 68 wide on the centre line y = 452: short, tall, medium, so that they
    // read as a voice and not as a pause symbol or a signal meter.
    private const double BarWidth = 68;
    private const double BarCenterY = 452;
    private static readonly (double CenterX, double Height)[] Bars = [(392, 150), (512, 270), (632, 200)];

    private static readonly Color White = Colors.White;
    private static readonly Color Accent = Rgb(0x0B57D0);

    /// <summary>The frames of the Windows icon, largest first (W-design 5.14).</summary>
    public static IReadOnlyList<int> IconSizes { get; } = [256, 128, 64, 48, 40, 32, 24, 20, 16];

    private static PathGeometry Bubble { get; } = CreateBubble();

    /// <summary>
    /// The iPhone icon: the master as an opaque square, full-bleed, with no rounded corners and no
    /// outer rim. The system applies its own mask.
    /// </summary>
    public static BitmapSource Master()
    {
        var picture = Render(MasterSize, MasterLayers(MasterSize, mask: null));
        var opaque = new FormatConvertedBitmap(picture, PixelFormats.Bgr24, null, 0);
        opaque.Freeze();
        return opaque;
    }

    /// <summary>One frame of the Windows icon, with transparency, drawn the way its size asks for.</summary>
    public static BitmapSource Frame(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 256);

        var visual = size switch
        {
            >= 128 => FullTile(size),
            >= 32 => FlatTile(size),
            _ => Glyph(size),
        };
        var straight = new FormatConvertedBitmap(Render(size, visual), PixelFormats.Bgra32, null, 0);
        straight.Freeze();
        return straight;
    }

    // ---- 256 and 128: the master on a rounded tile ------------------------------------------------

    /// <summary>
    /// The master, masked to a rounded square with a corner radius of 23.5% of the size, with an outer
    /// 1 px stroke #284682 at 0.16 and, inside it, a 1.1 px rim stroked with a vertical gradient of
    /// white: 1.0 at the top, 0.35 at 55%, 0.70 at the bottom.
    /// </summary>
    private static ContainerVisual FullTile(int size)
    {
        var radius = size * TileCornerShare;
        var mask = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius);
        var root = new ContainerVisual();
        root.Children.Add(MasterLayers(size, mask));

        var edges = new DrawingVisual();
        using (var context = edges.RenderOpen())
        {
            // A stroke lies half on each side of its line: the outer one is the outermost pixel, and
            // the rim begins where it ends.
            context.DrawRoundedRectangle(
                null,
                new Pen(new SolidColorBrush(Rgb(0x284682, 0.16)), 1),
                new Rect(0.5, 0.5, size - 1, size - 1),
                radius - 0.5,
                radius - 0.5);

            const double rim = 1.1;
            var inset = 1 + (rim / 2);
            var gradient = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, size),
                GradientStops =
                {
                    new GradientStop(Rgb(0xFFFFFF, 1.0), 0),
                    new GradientStop(Rgb(0xFFFFFF, 0.35), 0.55),
                    new GradientStop(Rgb(0xFFFFFF, 0.70), 1),
                },
            };
            context.DrawRoundedRectangle(
                null,
                new Pen(gradient, rim),
                new Rect(inset, inset, size - (2 * inset), size - (2 * inset)),
                radius - inset,
                radius - inset);
        }

        root.Children.Add(edges);
        return root;
    }

    /// <summary>
    /// The layers of the master at <paramref name="size"/> pixels, back to front: tile, bubble shadow,
    /// bubble, bars. With a mask everything is cut to it.
    /// </summary>
    private static ContainerVisual MasterLayers(int size, Geometry? mask)
    {
        var scale = (double)size / MasterSize;
        var layers = new ContainerVisual { Clip = mask };

        layers.Children.Add(Draw(scale, context =>
        {
            // Tile: a diagonal gradient, a bloom low on the right, a sheen across the top.
            context.DrawRectangle(
                Linear(new Point(128, 32), new Point(896, 992), (Rgb(0xFFFFFF), 0), (Rgb(0xE8F0FC), 0.5), (Rgb(0xC9DCF7), 1)),
                null,
                Canvas);
            context.DrawRectangle(
                new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    Center = new Point(832, 896),
                    GradientOrigin = new Point(832, 896),
                    RadiusX = 544,
                    RadiusY = 544,
                    GradientStops = { new GradientStop(Rgb(0x8DB9FF, 0.60), 0), new GradientStop(Rgb(0x8DB9FF, 0), 1) },
                },
                null,
                Canvas);
            context.PushClip(new RectangleGeometry(Canvas));
            context.DrawEllipse(
                Linear(new Point(0, 0), new Point(0, 416), (Rgb(0xFFFFFF, 0.70), 0), (Rgb(0xFFFFFF, 0), 1)),
                null,
                new Point(512, 32),
                704,
                320);
            context.Pop();
        }));

        // Bubble shadow: the bubble's shape, 40 lower, blurred by 80, in the accent colour at 0.30. The
        // blur is an effect of this layer alone, and its radius is given in pixels of the output.
        var shadow = Draw(scale, context =>
        {
            context.PushTransform(new TranslateTransform(0, 40));
            context.DrawGeometry(new SolidColorBrush(Rgb(0x0B57D0, 0.30)), null, Bubble);
            context.Pop();
        });
        shadow.Effect = new BlurEffect { Radius = 80 * scale, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
        layers.Children.Add(shadow);

        layers.Children.Add(Draw(scale, context =>
        {
            var top = Bubble.Bounds.Top;
            var bottom = Bubble.Bounds.Bottom;

            // Bubble: a vertical gradient over the height of the shape.
            context.DrawGeometry(
                Linear(new Point(0, top), new Point(0, bottom), (Rgb(0x3D82F0), 0), (Rgb(0x0B57D0), 0.6), (Rgb(0x0A47AE), 1)),
                null,
                Bubble);

            context.PushClip(Bubble);

            // Gloss: an ellipse turned by -12 degrees about its centre, white from 0.35 at its top to
            // nothing at its bottom. The gradient is drawn inside the turn, so it runs along the
            // ellipse's own axis.
            context.PushTransform(new RotateTransform(-12, 432, 290));
            context.DrawEllipse(
                Linear(new Point(432, 160), new Point(432, 420), (Rgb(0xFFFFFF, 0.35), 0), (Rgb(0xFFFFFF, 0), 1)),
                null,
                new Point(432, 290),
                250,
                130);
            context.Pop();

            // Rim: the outline, stroked 6 inside. A stroke of 12 on the outline, cut to the shape.
            context.DrawGeometry(
                null,
                new Pen(Linear(new Point(0, top), new Point(0, bottom), (Rgb(0xFFFFFF, 0.55), 0), (Rgb(0xFFFFFF, 0.10), 1)), 12),
                Bubble);
            context.Pop();
        }));

        layers.Children.Add(Draw(scale, context => DrawBars(context, new SolidColorBrush(White))));
        return layers;
    }

    // ---- 64 to 32: the same tile and mask, flat ---------------------------------------------------

    /// <summary>
    /// Tile #EAF1FB with a 1 px border #284682 at 0.20, the bubble solid #0B57D0, the bars white. No
    /// bloom, sheen, gloss, rim or shadow.
    /// </summary>
    private static ContainerVisual FlatTile(int size)
    {
        var scale = (double)size / MasterSize;
        var radius = size * TileCornerShare;
        var root = new ContainerVisual();

        var tile = new DrawingVisual();
        using (var context = tile.RenderOpen())
        {
            context.DrawRoundedRectangle(new SolidColorBrush(Rgb(0xEAF1FB)), null, new Rect(0, 0, size, size), radius, radius);
            context.DrawRoundedRectangle(
                null,
                new Pen(new SolidColorBrush(Rgb(0x284682, 0.20)), 1),
                new Rect(0.5, 0.5, size - 1, size - 1),
                radius - 0.5,
                radius - 0.5);
        }

        root.Children.Add(tile);

        // The tail keeps the master's outline. The body and the bars are put on whole pixels, or at
        // these sizes a bar 2 to 4 pixels wide would be a grey smear.
        var (body, bars, barRadius) = FlatPixels(size);
        var bubble = new SolidColorBrush(Accent);
        root.Children.Add(Draw(scale, context => context.DrawGeometry(bubble, null, Geometry.Parse(BubbleTail))));

        var glyph = new DrawingVisual();
        using (var context = glyph.RenderOpen())
        {
            context.DrawRoundedRectangle(bubble, null, body, BubbleRadius * scale, BubbleRadius * scale);
            var white = new SolidColorBrush(White);
            foreach (var bar in bars)
            {
                context.DrawRoundedRectangle(white, null, bar, barRadius, barRadius);
            }
        }

        root.Children.Add(glyph);
        return root;
    }

    /// <summary>
    /// The bubble's body and the three bars of a flat frame, in whole pixels. The bars keep the
    /// master's proportions as nearly as whole pixels allow: one width, one distance between them, and
    /// heights that are all even or all odd, so that the three share a centre line. The body takes the
    /// width and height nearest to the master's that put the bars exactly in its middle.
    /// </summary>
    private static (Rect Body, Rect[] Bars, double BarRadius) FlatPixels(int size)
    {
        var scale = (double)size / MasterSize;
        var centerX = (BubbleBody.Left + (BubbleBody.Width / 2)) * scale;
        var centerY = BarCenterY * scale;

        var barWidth = Math.Max(2, Whole(BarWidth * scale));
        var pitch = Math.Max(barWidth + 1, Whole((Bars[1].CenterX - Bars[0].CenterX) * scale));

        // Even heights or odd heights: whichever is nearer to the master's three.
        var heights = Bars.Select(bar => bar.Height * scale).ToArray();
        var parity = heights.Sum(height => Math.Abs(height - WithParity(height, 0))) <= heights.Sum(height => Math.Abs(height - WithParity(height, 1))) ? 0 : 1;

        var bodyWidth = WithParity(BubbleBody.Width * scale, barWidth % 2);
        var bodyHeight = WithParity(BubbleBody.Height * scale, parity);
        var body = new Rect(Whole(centerX - (bodyWidth / 2.0)), Whole(centerY - (bodyHeight / 2.0)), bodyWidth, bodyHeight);

        var firstLeft = body.Left + ((bodyWidth - ((2 * pitch) + barWidth)) / 2);
        var bars = heights
            .Select((height, index) =>
            {
                var whole = WithParity(height, parity);
                return new Rect(firstLeft + (index * pitch), body.Top + ((bodyHeight - whole) / 2), barWidth, whole);
            })
            .ToArray();

        // Round ends where a bar is wide enough to have them; a bar two pixels wide ends square.
        var radius = barWidth >= 4 ? barWidth / 2.0 : barWidth == 3 ? 1 : 0;
        return (body, bars, radius);
    }

    private static int Whole(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>The whole number nearest to <paramref name="value"/> that is even (0) or odd (1).</summary>
    private static int WithParity(double value, int parity) => parity + (2 * Whole((value - parity) / 2));

    // ---- 24 to 16: the glyph alone, on the pixel grid ---------------------------------------------

    /// <summary>
    /// The glyph in pixels of one small frame: the bubble's rectangle and corner radius, the three
    /// corners of the tail, and the three bars with square ends.
    /// </summary>
    private sealed record GlyphPixels(Rect Bubble, double Radius, Point[] Tail, Rect[] Bars);

    /// <summary>
    /// The 16 px frame has the coordinates the design system gives. The 20 and 24 px frames scale that
    /// drawing by 1.25 and 1.5 with every edge on a whole pixel again; their bars are 2 and 3 px wide,
    /// stay centred in the bubble and keep the order short, tall, medium.
    /// </summary>
    private static GlyphPixels PixelsFor(int size) => size switch
    {
        16 => new GlyphPixels(
            Edges(1, 1, 15, 12),
            3.5,
            [new Point(3, 12), new Point(2, 15), new Point(7, 12)],
            [Edges(4, 5, 6, 8), Edges(7, 3, 9, 10), Edges(10, 4, 12, 9)]),
        20 => new GlyphPixels(
            Edges(1, 1, 19, 15),
            4.375,
            [new Point(4, 15), new Point(3, 19), new Point(9, 15)],
            [Edges(5, 6, 7, 10), Edges(9, 4, 11, 12), Edges(13, 5, 15, 11)]),
        24 => new GlyphPixels(
            Edges(2, 2, 23, 18),
            5.25,
            [new Point(5, 18), new Point(4, 22), new Point(11, 18)],
            [Edges(6, 8, 9, 12), Edges(11, 5, 14, 15), Edges(16, 6, 19, 14)]),
        _ => throw new ArgumentOutOfRangeException(nameof(size), "The glyph is drawn at 16, 20 and 24 pixels."),
    };

    /// <summary>
    /// No tile, a transparent background: the bubble solid #1A66DE, which a dark taskbar and a light
    /// one both carry (3.10:1 and 4.73:1), and white bars.
    /// </summary>
    private static DrawingVisual Glyph(int size)
    {
        var pixels = PixelsFor(size);
        var bubble = new SolidColorBrush(Rgb(0x1A66DE));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(bubble, null, pixels.Bubble, pixels.Radius, pixels.Radius);

            var tail = new StreamGeometry();
            using (var figure = tail.Open())
            {
                figure.BeginFigure(pixels.Tail[0], isFilled: true, isClosed: true);
                figure.LineTo(pixels.Tail[1], isStroked: false, isSmoothJoin: false);
                figure.LineTo(pixels.Tail[2], isStroked: false, isSmoothJoin: false);
            }

            context.DrawGeometry(bubble, null, tail);

            var white = new SolidColorBrush(White);
            foreach (var bar in pixels.Bars)
            {
                context.DrawRectangle(white, null, bar);
            }
        }

        return visual;
    }

    // ---- Shared -----------------------------------------------------------------------------------

    private static void DrawBars(DrawingContext context, Brush brush)
    {
        foreach (var (centerX, height) in Bars)
        {
            context.DrawRoundedRectangle(
                brush,
                null,
                new Rect(centerX - (BarWidth / 2), BarCenterY - (height / 2), BarWidth, height),
                BarWidth / 2,
                BarWidth / 2);
        }
    }

    private static PathGeometry CreateBubble()
    {
        var bubble = Geometry.Combine(
            new RectangleGeometry(BubbleBody, BubbleRadius, BubbleRadius),
            Geometry.Parse(BubbleTail),
            GeometryCombineMode.Union,
            null);
        bubble.Freeze();
        return bubble;
    }

    /// <summary>A layer whose content is drawn in the units of the master, scaled to the output.</summary>
    private static DrawingVisual Draw(double scale, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(scale, scale));
            draw(context);
            context.Pop();
        }

        return visual;
    }

    private static RenderTargetBitmap Render(int size, Visual visual)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>A gradient between two points given in the units of what is drawn with it.</summary>
    private static LinearGradientBrush Linear(Point start, Point end, params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = start, EndPoint = end };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return brush;
    }

    /// <summary>A rectangle from its left, top, right and bottom edges.</summary>
    private static Rect Edges(double left, double top, double right, double bottom) => new(left, top, right - left, bottom - top);

    private static Color Rgb(int value, double alpha = 1) => Color.FromArgb(
        (byte)Math.Round(alpha * 255),
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)(value & 0xFF));
}
