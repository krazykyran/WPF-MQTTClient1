namespace WpfMqtt1.Models;

public sealed class AppSettings
{
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;
    public WindowBackdropPreference Backdrop { get; set; } = WindowBackdropPreference.Mica;
    public bool AutoRecordOnConnect { get; set; }
    public int MaxInMemoryMessages { get; set; } = 5000;
    public int DefaultPublishQos { get; set; }
    public PayloadFormat DefaultPayloadFormat { get; set; } = PayloadFormat.Text;
    public Guid? LastSelectedBrokerId { get; set; }

    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool IsWindowMaximized { get; set; }
}
