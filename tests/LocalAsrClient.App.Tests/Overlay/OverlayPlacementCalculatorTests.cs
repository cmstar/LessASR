using LocalAsrClient.App.Overlay;

namespace LocalAsrClient.App.Tests.Overlay;

public sealed class OverlayPlacementCalculatorTests
{
    [Fact]
    public void BottomCenter_UsesSecondaryMonitorWorkingArea()
    {
        var workingArea = new OverlayPixelRectangle(
            Left: 1920,
            Top: 0,
            Right: 4480,
            Bottom: 1400);

        var position = OverlayPlacementCalculator.BottomCenter(
            workingArea,
            windowWidth: 177,
            windowHeight: 48,
            topMargin: 24,
            bottomMargin: 23);

        Assert.Equal(new OverlayPixelPosition(3111, 1329), position);
    }

    [Fact]
    public void BottomCenter_SupportsMonitorToLeftOfPrimaryScreen()
    {
        var workingArea = new OverlayPixelRectangle(
            Left: -1920,
            Top: 0,
            Right: 0,
            Bottom: 1040);

        var position = OverlayPlacementCalculator.BottomCenter(
            workingArea,
            windowWidth: 118,
            windowHeight: 32,
            topMargin: 16,
            bottomMargin: 15);

        Assert.Equal(new OverlayPixelPosition(-1019, 993), position);
    }

    [Fact]
    public void BottomCenter_ClampsTopWhenOverlayIsTallerThanAvailableSpace()
    {
        var workingArea = new OverlayPixelRectangle(
            Left: 100,
            Top: 200,
            Right: 900,
            Bottom: 260);

        var position = OverlayPlacementCalculator.BottomCenter(
            workingArea,
            windowWidth: 500,
            windowHeight: 100,
            topMargin: 16,
            bottomMargin: 15);

        Assert.Equal(new OverlayPixelPosition(250, 216), position);
    }

    [Theory]
    [InlineData(15, 96, 15)]
    [InlineData(15, 144, 23)]
    [InlineData(16, 192, 32)]
    public void DevicePixels_ScalesMarginsForTargetMonitor(
        double deviceIndependentPixels,
        uint dpi,
        int expectedPixels)
    {
        Assert.Equal(
            expectedPixels,
            OverlayPlacementCalculator.DevicePixels(deviceIndependentPixels, dpi));
    }
}
