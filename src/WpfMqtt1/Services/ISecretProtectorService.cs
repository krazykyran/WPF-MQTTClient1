namespace WpfMqtt1.Services;

public interface ISecretProtectorService
{
    string? Protect(string? plaintext);
    string Unprotect(string? protectedBase64);
}
