using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.Views.Controls;

public partial class PayloadInspectorControl : UserControl
{
    private static readonly PayloadCodecService Codec = new();

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(MqttLogMessage),
            typeof(PayloadInspectorControl),
            new PropertyMetadata(null, OnInspectorInputChanged));

    public static readonly DependencyProperty SelectedFormatProperty =
        DependencyProperty.Register(
            nameof(SelectedFormat),
            typeof(PayloadFormat),
            typeof(PayloadInspectorControl),
            new FrameworkPropertyMetadata(
                PayloadFormat.Text,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnInspectorInputChanged));

    private static readonly DependencyPropertyKey HasMessagePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasMessage),
            typeof(bool),
            typeof(PayloadInspectorControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasMessageProperty = HasMessagePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey FormattedPayloadPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(FormattedPayload),
            typeof(string),
            typeof(PayloadInspectorControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FormattedPayloadProperty = FormattedPayloadPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey WarningMessagePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(WarningMessage),
            typeof(string),
            typeof(PayloadInspectorControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty WarningMessageProperty = WarningMessagePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey EffectiveTextWrappingPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(EffectiveTextWrapping),
            typeof(TextWrapping),
            typeof(PayloadInspectorControl),
            new PropertyMetadata(TextWrapping.Wrap));

    public static readonly DependencyProperty EffectiveTextWrappingProperty = EffectiveTextWrappingPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CopyPayloadTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CopyPayloadText),
            typeof(string),
            typeof(PayloadInspectorControl),
            new PropertyMetadata("Copy"));

    public static readonly DependencyProperty CopyPayloadTextProperty = CopyPayloadTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CopyTopicTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CopyTopicText),
            typeof(string),
            typeof(PayloadInspectorControl),
            new PropertyMetadata("Topic"));

    public static readonly DependencyProperty CopyTopicTextProperty = CopyTopicTextPropertyKey.DependencyProperty;

    public MqttLogMessage? Message
    {
        get => (MqttLogMessage?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public PayloadFormat SelectedFormat
    {
        get => (PayloadFormat)GetValue(SelectedFormatProperty);
        set => SetValue(SelectedFormatProperty, value);
    }

    public bool HasMessage => (bool)GetValue(HasMessageProperty);
    public string FormattedPayload => (string)GetValue(FormattedPayloadProperty);
    public string? WarningMessage => (string?)GetValue(WarningMessageProperty);
    public TextWrapping EffectiveTextWrapping => (TextWrapping)GetValue(EffectiveTextWrappingProperty);
    public string CopyPayloadText => (string)GetValue(CopyPayloadTextProperty);
    public string CopyTopicText => (string)GetValue(CopyTopicTextProperty);

    public PayloadInspectorControl()
    {
        InitializeComponent();
        UpdateFormatButtonStyles();
    }

    private static void OnInspectorInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PayloadInspectorControl control)
        {
            control.RefreshView();
        }
    }

    private void RefreshView()
    {
        UpdateFormatButtonStyles();

        var msg = Message;
        SetValue(HasMessagePropertyKey, msg is not null);
        SetValue(
            EffectiveTextWrappingPropertyKey,
            SelectedFormat == PayloadFormat.Hex ? TextWrapping.NoWrap : TextWrapping.Wrap);

        if (msg is null)
        {
            SetValue(FormattedPayloadPropertyKey, string.Empty);
            SetValue(WarningMessagePropertyKey, null);
            return;
        }

        var formatted = Codec.FormatPayload(msg.Payload, SelectedFormat);
        SetValue(FormattedPayloadPropertyKey, formatted.FormattedText);
        SetValue(WarningMessagePropertyKey, formatted.WarningMessage);
    }

    private void UpdateFormatButtonStyles()
    {
        if (TextFormatButton is null)
        {
            return;
        }

        TextFormatButton.Appearance = SelectedFormat == PayloadFormat.Text ? ControlAppearance.Primary : ControlAppearance.Secondary;
        JsonFormatButton.Appearance = SelectedFormat == PayloadFormat.Json ? ControlAppearance.Primary : ControlAppearance.Secondary;
        HexFormatButton.Appearance = SelectedFormat == PayloadFormat.Hex ? ControlAppearance.Primary : ControlAppearance.Secondary;
        Base64FormatButton.Appearance = SelectedFormat == PayloadFormat.Base64 ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    private void OnFormatButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } &&
            Enum.TryParse<PayloadFormat>(tag, out var format))
        {
            SelectedFormat = format;
        }
    }

    private async void OnCopyPayloadClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(FormattedPayload ?? string.Empty);
            SetValue(CopyPayloadTextPropertyKey, "Copied");
            await Task.Delay(1200);
            SetValue(CopyPayloadTextPropertyKey, "Copy");
        }
        catch
        {
            // Ignore transient clipboard lock errors.
        }
    }

    private async void OnCopyTopicClick(object sender, RoutedEventArgs e)
    {
        if (Message is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(Message.Topic);
            SetValue(CopyTopicTextPropertyKey, "Copied");
            await Task.Delay(1200);
            SetValue(CopyTopicTextPropertyKey, "Topic");
        }
        catch
        {
            // Ignore transient clipboard lock errors.
        }
    }
}
