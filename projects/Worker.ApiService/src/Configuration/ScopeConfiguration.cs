using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Pulsas.Worker.ApiService.Configuration;

public static class ScopeConfiguration
{
    public static ImmutableArray<ConfiguredTransportScope> Read(IConfiguration configuration)
    {
        var result = ImmutableArray.CreateBuilder<ConfiguredTransportScope>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in ConfigurationValues.List(configuration.GetSection("scopes")))
        {
            ConfigurationValues.Members(section, "name", "key");
            var name = ConfigurationValues.Text(section.GetSection("name"));
            var privateScope = name.StartsWith('$');
            if (
                privateScope
                && (name.Length == 1 || name[1] == '$' || string.IsNullOrWhiteSpace(name[1..]))
            )
            {
                throw new InvalidOperationException(
                    "A private scope requires a name and one leading $."
                );
            }
            name = privateScope ? name : ConfigurationValues.PublicName(name);
            if (!names.Add(name))
            {
                throw new InvalidOperationException("Duplicate transport scope name.");
            }
            if (
                !privateScope
                && section
                    .GetChildren()
                    .Any(child => child.Key.Equals("key", StringComparison.OrdinalIgnoreCase))
            )
            {
                throw new InvalidOperationException(
                    "Only private transport scopes accept a configured Key."
                );
            }
            var key = privateScope
                ? ConfigurationKeys.Read(section, allow32Bytes: false)
                : SHA256.HashData(Encoding.UTF8.GetBytes(name))[..16];
            try
            {
                result.Add(new(name, key));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        return result.ToImmutable();
    }
}
