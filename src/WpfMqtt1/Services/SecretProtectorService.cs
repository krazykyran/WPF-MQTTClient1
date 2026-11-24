using System.Security.Cryptography;
using System.Text;

namespace WpfMqtt1.Services;

public sealed class SecretProtectorService : ISecretProtectorService
{
    private static readonly byte[] AppEntropy = "WpfMqtt1.BrokerCredentials.v1"u8.ToArray();

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        var rawBytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(rawBytes, AppEntropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var rawBytes = ProtectedData.Unprotect(protectedBytes, AppEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(rawBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return string.Empty;
        }
    }
}
