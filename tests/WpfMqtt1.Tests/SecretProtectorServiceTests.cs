using WpfMqtt1.Services;

namespace WpfMqtt1.Tests;

public sealed class SecretProtectorServiceTests
{
    private readonly SecretProtectorService _sut = new();

    [Fact]
    public void ProtectAndUnprotect_RoundTripsPlaintext()
    {
        const string secret = "s3cr3t-br0k3r-P@ssw0rd!";

        var cipher = _sut.Protect(secret);

        Assert.False(string.IsNullOrWhiteSpace(cipher));
        Assert.NotEqual(secret, cipher);
        Assert.DoesNotContain(secret, cipher, StringComparison.Ordinal);

        var decrypted = _sut.Unprotect(cipher);
        Assert.Equal(secret, decrypted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Protect_NullOrEmpty_ReturnsNull(string? input)
    {
        Assert.Null(_sut.Protect(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-valid-base64!!!")]
    [InlineData("QUJDREVGRw==")] // Valid base64, invalid DPAPI blob
    public void Unprotect_InvalidOrEmpty_ReturnsEmptyString(string? input)
    {
        Assert.Equal(string.Empty, _sut.Unprotect(input));
    }
}
