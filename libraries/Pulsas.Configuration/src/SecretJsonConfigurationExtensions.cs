using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Pulsas.Configuration;

public static class SecretJsonConfigurationExtensions
{
    // Startup snapshot: later providers may override values; files are never watched or reopened.
    public static IConfigurationBuilder AddSecretJsonFiles(
        this IConfigurationBuilder builder,
        string? path
    )
    {
        if (path is null)
        {
            return builder;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var attributes = File.GetAttributes(fullPath);
        var files = attributes.HasFlag(FileAttributes.Directory)
            ? Directory.GetFiles(fullPath, "*.json", SearchOption.TopDirectoryOnly)
            : [fullPath];
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0)
        {
            throw new FormatException($"Secret directory '{fullPath}' contains no JSON files.");
        }

        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            using var document = ParseDocument(text, file);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"Secret file '{file}' must contain a JSON object.");
            }

            var declarations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!declarations.Add(property.Name))
                {
                    throw new FormatException(
                        $"Secret file '{file}' contains duplicate top-level declarations."
                    );
                }
                var delimiter = property.Name.IndexOf(
                    ConfigurationPath.KeyDelimiter,
                    StringComparison.Ordinal
                );
                sections.Add(delimiter < 0 ? property.Name : property.Name[..delimiter]);
            }
            foreach (var section in sections)
            {
                if (owners.TryGetValue(section, out var owner))
                {
                    throw new FormatException(
                        $"Secret files '{owner}' and '{file}' declare the same top-level configuration section."
                    );
                }
                owners.Add(section, file);
            }
            ReadValues(text, file, snapshot);
        }

        // Do not mutate the caller until all selected documents have passed validation.
        return builder.AddInMemoryCollection(snapshot);
    }

    private static JsonDocument ParseDocument(string text, string file)
    {
        try
        {
            return JsonDocument.Parse(
                text,
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }
            );
        }
        catch (JsonException)
        {
            // Parser diagnostics can quote secrets; omit their exception chain.
            throw new FormatException($"Secret file '{file}' contains invalid JSON.");
        }
    }

    private static void ReadValues(string text, string file, Dictionary<string, string?> snapshot)
    {
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
            using var lifetime = (IDisposable)configuration;
            var provider = configuration.Providers.Single();
            foreach (var entry in configuration.AsEnumerable())
            {
                // Preserve explicit nulls, but not implicit parent keys.
                if (provider.TryGet(entry.Key, out var value))
                {
                    snapshot.Add(entry.Key, value);
                }
            }
        }
        catch (FormatException)
        {
            throw new FormatException(
                $"Secret file '{file}' contains invalid or duplicate configuration keys."
            );
        }
    }
}
