using LiveCue.Desktop.Controls;

namespace LiveCue.Desktop.Tests.Pairing;

/// <summary>
/// The arithmetic of the QR's drawing (test plan 2.8; W-design 5.6; design system 4.15): a module is a
/// whole number of device pixels and at least 3, the quiet zone is four modules on every side, and the
/// drawn square is as close to 240 layout units as that allows and never under 200.
/// </summary>
public sealed class QrGeometryTests
{
    [Theory]

    // modules per side, device scale, module in device pixels, drawn side in device pixels
    [InlineData(21, 1.0, 8, 232)]    // version 1: floor(240 / 29) = 8
    [InlineData(25, 1.0, 7, 231)]
    [InlineData(29, 1.0, 6, 222)]
    [InlineData(33, 1.0, 5, 205)]
    [InlineData(37, 1.0, 5, 225)]
    [InlineData(41, 1.0, 5, 245)]    // floor(240 / 49) = 4 would be 196 wide, under 200: one pixel more
    [InlineData(45, 1.0, 4, 212)]
    [InlineData(49, 1.0, 4, 228)]
    [InlineData(53, 1.0, 4, 244)]    // 3 would be 183 wide
    [InlineData(57, 1.0, 4, 260)]    // 3 would be 195 wide
    [InlineData(61, 1.0, 3, 207)]
    [InlineData(73, 1.0, 3, 243)]    // floor(240 / 81) = 2: never under 3
    [InlineData(177, 1.0, 3, 555)]   // the largest code still has modules of 3
    [InlineData(41, 1.25, 6, 294)]   // floor(300 / 49) = 6: 235.2 units
    [InlineData(41, 1.5, 7, 343)]    // floor(360 / 49) = 7: 228.7 units
    [InlineData(41, 2.0, 9, 441)]    // floor(480 / 49) = 9: 220.5 units
    [InlineData(57, 1.5, 5, 325)]    // floor(360 / 65) = 5: 216.7 units
    [InlineData(57, 2.0, 7, 455)]
    [InlineData(33, 1.5, 8, 328)]
    public void Module_size_cases(int modules, double scale, int modulePixels, int sidePixels)
    {
        Assert.Equal(modulePixels, QrGeometry.ModulePixels(modules, scale));
        Assert.Equal(sidePixels, QrGeometry.SidePixels(modules, scale));

        // The square is the code and a quiet zone of four modules on every side.
        Assert.Equal(4, QrGeometry.QuietZoneModules);
        Assert.Equal(modules + 8, QrGeometry.DrawnModules(modules));
        Assert.Equal(modulePixels * (modules + 8), sidePixels);

        // Whole device pixels, at least 3, and never narrower than 200 layout units.
        Assert.True(modulePixels >= QrGeometry.MinimumModulePixels);
        var side = QrGeometry.Side(modules, scale);
        Assert.Equal(sidePixels / scale, side, 9);
        Assert.True(side >= QrGeometry.MinimumSide);

        // One pixel less would break a rule: under 3 pixels, or under 200 units, or it was already
        // the largest that fits in 240.
        var smaller = modulePixels - 1;
        var fitsInTarget = modulePixels * (modules + 8) / scale <= QrGeometry.TargetSide;
        Assert.True(
            fitsInTarget
            || smaller < QrGeometry.MinimumModulePixels
            || smaller * (modules + 8) / scale < QrGeometry.MinimumSide);
        if (fitsInTarget)
        {
            Assert.True((modulePixels + 1) * (modules + 8) / scale > QrGeometry.TargetSide);
        }
    }

    [Fact]
    public void A_matrix_or_a_scale_that_cannot_be_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QrGeometry.ModulePixels(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => QrGeometry.ModulePixels(41, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => QrGeometry.ModulePixels(41, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => QrGeometry.ModulePixels(41, double.PositiveInfinity));
    }
}
