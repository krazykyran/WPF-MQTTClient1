using System.IO;
using WpfMqtt1.Services;

namespace WpfMqtt1.Tests;

public sealed class Phase5PolishTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_Phase5_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WindowBoundsHelper_RestoresValidSavedPlacement()
    {
        var resolved = WindowBoundsHelper.Resolve(
            savedWidth: 1440,
            savedHeight: 900,
            savedLeft: 120,
            savedTop: 80,
            isMaximized: true,
            minWidth: 960,
            minHeight: 640,
            virtualLeft: 0,
            virtualTop: 0,
            virtualWidth: 2560,
            virtualHeight: 1440);

        Assert.Equal(1440, resolved.Width);
        Assert.Equal(900, resolved.Height);
        Assert.Equal(120, resolved.Left);
        Assert.Equal(80, resolved.Top);
        Assert.True(resolved.IsMaximized);
    }

    [Fact]
    public void WindowBoundsHelper_ResetsPositionWhenMonitorDisconnected()
    {
        var resolved = WindowBoundsHelper.Resolve(
            savedWidth: 1300,
            savedHeight: 820,
            savedLeft: 3200, // Off-screen on disconnected second monitor
            savedTop: 100,
            isMaximized: false,
            minWidth: 960,
            minHeight: 640,
            virtualLeft: 0,
            virtualTop: 0,
            virtualWidth: 1920,
            virtualHeight: 1080);

        Assert.Equal(1300, resolved.Width);
        Assert.Equal(820, resolved.Height);
        Assert.Null(resolved.Left);
        Assert.Null(resolved.Top);
    }

    [Fact]
    public void WindowBoundsHelper_ClampsBelowMinimumAndAboveVirtualScreen()
    {
        var resolved = WindowBoundsHelper.Resolve(
            savedWidth: 400,
            savedHeight: 5000,
            savedLeft: 50,
            savedTop: 50,
            isMaximized: false,
            minWidth: 960,
            minHeight: 640,
            virtualLeft: 0,
            virtualTop: 0,
            virtualWidth: 1920,
            virtualHeight: 1080);

        Assert.Equal(960, resolved.Width);
        Assert.Equal(1080, resolved.Height);
    }

    [Fact]
    public void ErrorLogService_WritesStructuredEntryToDisk()
    {
        var paths = new AppPathsService(_tempDir);
        var logger = new ErrorLogService(paths);

        logger.LogException("UnitTestSource", new InvalidOperationException("Simulated fault"));

        Assert.True(File.Exists(paths.ErrorLogFilePath));
        var content = File.ReadAllText(paths.ErrorLogFilePath);
        Assert.Contains("UnitTestSource", content);
        Assert.Contains("Simulated fault", content);
        Assert.Contains("InvalidOperationException", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
