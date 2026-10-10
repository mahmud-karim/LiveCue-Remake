using System.Windows.Markup;
using System.Windows.Media;

namespace LiveCue.Desktop.Theming;

/// <summary>
/// The colour to give a <see cref="System.Windows.Media.Effects.DropShadowEffect"/> so that the shadow
/// it draws has the colour you mean.
///
/// WPF hands the effect's colour to its shader as linear-light values and then blends the result as
/// if those numbers were ordinary sRGB. A shadow asked for in #283C6E is therefore drawn in about
/// #050B28: much darker and less blue than the token. Passing the sRGB-encoded form of each channel
/// cancels that out. The desktop self-test proves the result: with this in place a panel on the flat
/// ground composites to the design system's reference colour.
///
/// Use: <c>Color="{theming:ShadowColor Token={StaticResource ShadeColor}}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(Color))]
public sealed class ShadowColorExtension : MarkupExtension
{
    /// <summary>The colour the shadow should appear in (the design token, plain sRGB).</summary>
    public Color Token { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Compensate(Token);

    /// <summary>Returns the colour whose linear-light reading equals <paramref name="wanted"/>.</summary>
    public static Color Compensate(Color wanted) =>
        Color.FromArgb(wanted.A, Encode(wanted.R), Encode(wanted.G), Encode(wanted.B));

    private static byte Encode(byte channel)
    {
        var value = channel / 255.0;
        var encoded = value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
        return (byte)Math.Round(Math.Clamp(encoded, 0, 1) * 255);
    }
}
