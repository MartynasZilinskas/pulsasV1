using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Pulsas.Worker.ApiService.Configuration;

internal static class ConfigurationSnapshot
{
    private static readonly string[] Catalogues =
    [
        "chat:hashtags",
        "chat:explicit",
        "scopes",
        "regions",
        "region_groups",
        "default_view",
    ];

    // Worker startup uses JSON/in-memory declarations and standard environment/CLI patches.
    internal static ConfigurationRoot Read(IConfigurationRoot configuration)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in configuration.Providers)
        {
            var entries = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            ReadProvider(provider, null, entries);
            if (
                provider
                is not EnvironmentVariablesConfigurationProvider
                    and not CommandLineConfigurationProvider
            )
            {
                foreach (var catalogue in Catalogues)
                {
                    if (entries.Keys.Any(key => Within(key, catalogue)))
                    {
                        foreach (
                            var key in values.Keys.Where(key => Within(key, catalogue)).ToArray()
                        )
                        {
                            values.Remove(key);
                        }
                    }
                }
            }
            foreach (var entry in entries)
            {
                values[entry.Key] = entry.Value;
            }
        }
        foreach (var legacy in new[] { "chat:channels", "transport:scopes" })
        {
            if (values.Keys.Any(key => Within(key, legacy)))
            {
                throw new InvalidOperationException(
                    "Legacy chat/channel or transport/scope configuration must be migrated to the compact format."
                );
            }
        }
        return (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static bool Within(string key, string path) =>
        key.Equals(path, StringComparison.OrdinalIgnoreCase)
        || key.StartsWith(path + ":", StringComparison.OrdinalIgnoreCase);

    private static void ReadProvider(
        IConfigurationProvider provider,
        string? parent,
        Dictionary<string, string?> values
    )
    {
        foreach (
            var child in provider
                .GetChildKeys([], parent)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        )
        {
            var path = parent is null ? child : parent + ":" + child;
            if (provider.TryGet(path, out var value))
            {
                values[path] = value;
            }
            ReadProvider(provider, path, values);
        }
    }
}
