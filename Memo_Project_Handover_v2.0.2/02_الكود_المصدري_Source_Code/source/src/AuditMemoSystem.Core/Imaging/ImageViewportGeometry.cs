namespace AuditMemoSystem.Core.Imaging;

public enum ImageViewportMode
{
    Fit,
    CustomZoom
}

public readonly record struct PixelSize(double Width, double Height)
{
    public bool IsPositive => Width > 0d && Height > 0d;
}

public readonly record struct ViewportPoint(double X, double Y);

public readonly record struct ImageViewportState(ImageViewportMode Mode, double Zoom, int RotationDegrees)
{
    public static ImageViewportState Fit => new(ImageViewportMode.Fit, 1d, 0);
    public static ImageViewportState ActualSize => new(ImageViewportMode.CustomZoom, 1d, 0);
}

public readonly record struct ImageViewportLayout(
    PixelSize RotatedSourceSize,
    PixelSize DisplaySize,
    ViewportPoint Origin,
    double EffectiveZoom);

public static class ImageViewportGeometry
{
    public const double MinimumZoom = 0.25d;
    public const double MaximumZoom = 4d;
    public const double ZoomStep = 0.15d;

    public static ImageViewportLayout Calculate(
        PixelSize sourceSize,
        PixelSize viewportSize,
        ImageViewportState state)
    {
        if (!sourceSize.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSize), "Image dimensions must be positive.");
        }

        var rotation = NormalizeRotation(state.RotationDegrees);
        var rotated = rotation is 90 or 270
            ? new PixelSize(sourceSize.Height, sourceSize.Width)
            : sourceSize;
        var zoom = state.Mode == ImageViewportMode.Fit
            ? CalculateFitZoom(rotated, viewportSize)
            : ClampZoom(state.Zoom);
        var display = Scale(rotated, zoom);
        var origin = new ViewportPoint(
            Math.Max(0d, (viewportSize.Width - display.Width) / 2d),
            Math.Max(0d, (viewportSize.Height - display.Height) / 2d));

        return new ImageViewportLayout(rotated, display, origin, zoom);
    }

    public static ImageViewportState ChangeZoom(
        PixelSize sourceSize,
        PixelSize viewportSize,
        ImageViewportState state,
        double delta)
    {
        if (delta == 0d || !double.IsFinite(delta))
        {
            return state;
        }

        var current = Calculate(sourceSize, viewportSize, state).EffectiveZoom;
        if ((delta < 0d && current <= MinimumZoom) || (delta > 0d && current >= MaximumZoom))
        {
            return state;
        }

        return state with
        {
            Mode = ImageViewportMode.CustomZoom,
            Zoom = ClampZoom(current + delta)
        };
    }

    public static ImageViewportState Rotate(ImageViewportState state, int degrees) =>
        state with { RotationDegrees = NormalizeRotation(state.RotationDegrees + degrees) };

    public static int NormalizeRotation(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized % 90 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), "Rotation must be a multiple of 90 degrees.");
        }

        return normalized;
    }

    public static double ClampZoom(double zoom) =>
        Math.Clamp(double.IsFinite(zoom) ? zoom : 1d, MinimumZoom, MaximumZoom);

    public static int ClampPanOffset(int requestedOffset, int contentLength, int viewportLength)
    {
        var maximum = Math.Max(0, contentLength - Math.Max(0, viewportLength));
        return Math.Clamp(requestedOffset, 0, maximum);
    }

    private static double CalculateFitZoom(PixelSize source, PixelSize viewport)
    {
        if (!viewport.IsPositive)
        {
            return 1d;
        }

        return Math.Min(MaximumZoom, Math.Min(viewport.Width / source.Width, viewport.Height / source.Height));
    }

    private static PixelSize Scale(PixelSize size, double scale) => new(
        Math.Max(1d, Math.Round(size.Width * scale)),
        Math.Max(1d, Math.Round(size.Height * scale)));
}
