using AuditMemoSystem.Core.Imaging;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class ImageViewportGeometryTests
{
    [Fact]
    public void Fit_CentersLandscapeImageWithoutCropping()
    {
        var layout = ImageViewportGeometry.Calculate(
            new PixelSize(1600, 900),
            new PixelSize(800, 600),
            ImageViewportState.Fit);

        Assert.Equal(0.5d, layout.EffectiveZoom, 6);
        Assert.Equal(new PixelSize(800, 450), layout.DisplaySize);
        Assert.Equal(new ViewportPoint(0, 75), layout.Origin);
    }

    [Fact]
    public void Fit_AndActualSize_AreDistinctModes()
    {
        var fit = ImageViewportGeometry.Calculate(
            new PixelSize(100, 50),
            new PixelSize(800, 600),
            ImageViewportState.Fit);
        var actual = ImageViewportGeometry.Calculate(
            new PixelSize(100, 50),
            new PixelSize(800, 600),
            ImageViewportState.ActualSize);

        Assert.Equal(4d, fit.EffectiveZoom);
        Assert.Equal(new PixelSize(400, 200), fit.DisplaySize);
        Assert.Equal(1d, actual.EffectiveZoom);
        Assert.Equal(new PixelSize(100, 50), actual.DisplaySize);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void QuarterTurns_SwapSourceDimensions(int rotation)
    {
        var layout = ImageViewportGeometry.Calculate(
            new PixelSize(1200, 800),
            new PixelSize(600, 600),
            new ImageViewportState(ImageViewportMode.Fit, 1d, rotation));

        Assert.Equal(new PixelSize(800, 1200), layout.RotatedSourceSize);
        Assert.Equal(new PixelSize(400, 600), layout.DisplaySize);
    }

    [Fact]
    public void ZoomFromFit_UsesVisibleFitScaleAsStartingPoint()
    {
        var state = ImageViewportGeometry.ChangeZoom(
            new PixelSize(1600, 900),
            new PixelSize(800, 600),
            ImageViewportState.Fit,
            ImageViewportGeometry.ZoomStep);

        Assert.Equal(ImageViewportMode.CustomZoom, state.Mode);
        Assert.Equal(0.65d, state.Zoom, 6);
        var layout = ImageViewportGeometry.Calculate(
            new PixelSize(1600, 900),
            new PixelSize(800, 600),
            state);
        Assert.Equal(new PixelSize(1040, 585), layout.DisplaySize);
    }

    [Theory]
    [InlineData(0.25, -0.15, 0.25)]
    [InlineData(4.0, 0.15, 4.0)]
    public void ZoomBounds_AreStable(double zoom, double delta, double expected)
    {
        var original = new ImageViewportState(ImageViewportMode.CustomZoom, zoom, 0);
        var changed = ImageViewportGeometry.ChangeZoom(
            new PixelSize(100, 100),
            new PixelSize(100, 100),
            original,
            delta);

        Assert.Equal(expected, changed.Zoom, 6);
    }

    [Theory]
    [InlineData(-20, 800, 400, 0)]
    [InlineData(250, 800, 400, 250)]
    [InlineData(900, 800, 400, 400)]
    [InlineData(50, 200, 400, 0)]
    public void PanOffset_IsClampedToScrollableRange(
        int requested,
        int content,
        int viewport,
        int expected)
    {
        Assert.Equal(expected, ImageViewportGeometry.ClampPanOffset(requested, content, viewport));
    }

    [Fact]
    public void Rotation_NormalizesQuarterTurns_AndRejectsOtherAngles()
    {
        Assert.Equal(270, ImageViewportGeometry.NormalizeRotation(-90));
        Assert.Equal(90, ImageViewportGeometry.NormalizeRotation(450));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageViewportGeometry.NormalizeRotation(45));
    }

    [Theory]
    [InlineData(0.1d, 0.25d)]
    [InlineData(8d, 4d)]
    [InlineData(double.NaN, 1d)]
    [InlineData(double.PositiveInfinity, 1d)]
    public void ClampZoom_HandlesLimitsAndNonFiniteValues(double value, double expected)
    {
        Assert.Equal(expected, ImageViewportGeometry.ClampZoom(value));
    }

    [Fact]
    public void Calculate_RejectsNonPositiveSourceSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageViewportGeometry.Calculate(
            new PixelSize(0, 100),
            new PixelSize(800, 600),
            ImageViewportState.Fit));
    }
}
