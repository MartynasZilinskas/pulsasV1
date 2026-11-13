using System.Security.Cryptography;

namespace Pulsas.Worker.ApiService.Configuration;

internal static class ConfigurationKeys
{
    internal static byte[] Read(IConfiguration section, bool allow32Bytes)
    {
        if (section.GetSection("key").GetChildren().Any())
        {
            throw new InvalidOperationException(
                "Configured encryption keys must be Base64 strings."
            );
        }
        byte[] key;
        try
        {
            key = Convert.FromBase64String(section["Key"] ?? "");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Invalid configured encryption key; expected Base64 key material."
            );
        }
        if (key.Length != 16 && !(allow32Bytes && key.Length == 32))
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                allow32Bytes
                    ? "Configured channel keys must contain 16 or 32 bytes."
                    : "Configured transport-scope keys must contain 16 bytes."
            );
        }
        return key;
    }
}
