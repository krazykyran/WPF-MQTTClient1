namespace WpfMqtt1.Services;

public readonly record struct ResolvedWindowBounds(
    double Width,
    double Height,
    double? Left,
    double? Top,
    bool IsMaximized);

public static class WindowBoundsHelper
{
    public const double DefaultWidth = 1280;
    public const double DefaultHeight = 800;

    public static ResolvedWindowBounds Resolve(
        double? savedWidth,
        double? savedHeight,
        double? savedLeft,
        double? savedTop,
        bool isMaximized,
        double minWidth,
        double minHeight,
        double virtualLeft,
        double virtualTop,
        double virtualWidth,
        double virtualHeight)
    {
        var maxUsableWidth = Math.Max(minWidth, virtualWidth);
        var maxUsableHeight = Math.Max(minHeight, virtualHeight);

        var width = savedWidth is > 0 && !double.IsNaN(savedWidth.Value) && !double.IsInfinity(savedWidth.Value)
            ? Math.Clamp(savedWidth.Value, minWidth, maxUsableWidth)
            : Math.Min(DefaultWidth, maxUsableWidth);

        var height = savedHeight is > 0 && !double.IsNaN(savedHeight.Value) && !double.IsInfinity(savedHeight.Value)
            ? Math.Clamp(savedHeight.Value, minHeight, maxUsableHeight)
            : Math.Min(DefaultHeight, maxUsableHeight);

        double? left = null;
        double? top = null;

        if (savedLeft.HasValue && savedTop.HasValue &&
            !double.IsNaN(savedLeft.Value) && !double.IsInfinity(savedLeft.Value) &&
            !double.IsNaN(savedTop.Value) && !double.IsInfinity(savedTop.Value))
        {
            const double minVisibleEdge = 80;
            var right = savedLeft.Value + width;
            var bottom = savedTop.Value + height;

            var virtualRight = virtualLeft + virtualWidth;
            var virtualBottom = virtualTop + virtualHeight;

            var visibleHorizontally = right >= virtualLeft + minVisibleEdge && savedLeft.Value <= virtualRight - minVisibleEdge;
            var visibleVertically = bottom >= virtualTop + minVisibleEdge && savedTop.Value <= virtualBottom - minVisibleEdge;

            if (visibleHorizontally && visibleVertically)
            {
                left = savedLeft.Value;
                top = savedTop.Value;
            }
        }

        return new ResolvedWindowBounds(width, height, left, top, isMaximized);
    }
}
