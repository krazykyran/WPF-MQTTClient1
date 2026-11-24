using System.Text;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.Tests;

public sealed class PayloadCodecServiceTests
{
    private readonly PayloadCodecService _sut = new();

    [Fact]
    public void FormatPayload_Json_PrettyPrintsValidJson()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"temp":22.5,"ok":true}""");

        var result = _sut.FormatPayload(bytes, PayloadFormat.Json);

        Assert.True(result.IsValidFormat);
        Assert.Null(result.WarningMessage);
        Assert.Contains("\n", result.FormattedText);
        Assert.Contains("\"temp\": 22.5", result.FormattedText);
    }

    [Fact]
    public void FormatPayload_Json_FallsBackWithWarningOnInvalidJson()
    {
        var bytes = Encoding.UTF8.GetBytes("hello plain text");

        var result = _sut.FormatPayload(bytes, PayloadFormat.Json);

        Assert.False(result.IsValidFormat);
        Assert.NotNull(result.WarningMessage);
        Assert.Equal("hello plain text", result.FormattedText);
    }

    [Fact]
    public void FormatPayload_Hex_ProducesOffsetHexAndAsciiSidebar()
    {
        var bytes = new byte[] { 0x48, 0x69, 0x00, 0xFF };

        var result = _sut.FormatPayload(bytes, PayloadFormat.Hex);

        Assert.True(result.IsValidFormat);
        Assert.Contains("00000000", result.FormattedText);
        Assert.Contains("48 69 00 FF", result.FormattedText);
        Assert.Contains("|Hi..|", result.FormattedText);
    }

    [Fact]
    public void FormatPayload_Base64_RoundTripsViaTryEncodePayload()
    {
        var original = new byte[] { 0x01, 0x02, 0xFE, 0xFF, 0x42 };

        var formatted = _sut.FormatPayload(original, PayloadFormat.Base64);
        var ok = _sut.TryEncodePayload(formatted.FormattedText, PayloadFormat.Base64, out var decoded, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData("0xDE 0xAD 0xBE 0xEF", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    [InlineData("de:ad:be:ef", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    [InlineData("DE-AD-BE-EF", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    [InlineData("deadbeef", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    public void TryEncodePayload_Hex_AcceptsFlexibleSeparators(string input, byte[] expected)
    {
        var ok = _sut.TryEncodePayload(input, PayloadFormat.Hex, out var bytes, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("ZZ 00")]
    public void TryEncodePayload_Hex_RejectsInvalidInput(string input)
    {
        var ok = _sut.TryEncodePayload(input, PayloadFormat.Hex, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryEncodePayload_Json_RejectsMalformedJson()
    {
        var ok = _sut.TryEncodePayload("{bad json", PayloadFormat.Json, out _, out var error);

        Assert.False(ok);
        Assert.Contains("Invalid JSON", error);
    }

    [Fact]
    public void TryFormatJson_FormatsValidJson()
    {
        var ok = _sut.TryFormatJson("{\"a\":1,\"b\":[2,3]}", out var formatted, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Contains("\"a\": 1", formatted);
        Assert.Contains("\n", formatted);
    }

    [Fact]
    public void CreatePreview_FormatsTextAndBinaryDifferently()
    {
        var textPreview = _sut.CreatePreview(Encoding.UTF8.GetBytes("line1\n  line2\tline3"));
        Assert.Equal("line1 line2 line3", textPreview);

        var binaryPreview = _sut.CreatePreview([0x00, 0xFF, 0x0A, 0x7F]);
        Assert.StartsWith("[Binary 4 B]", binaryPreview);
        Assert.Contains("00 FF 0A 7F", binaryPreview);
    }
}
