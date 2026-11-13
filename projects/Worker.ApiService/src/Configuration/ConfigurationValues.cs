using System.Globalization;

namespace Pulsas.Worker.ApiService.Configuration;

internal static class ConfigurationValues
{
    internal static void Members(IConfigurationSection section, params string[] allowed)
    {
        if (
            section.Value is not null
            || section
                .GetChildren()
                .Any(child => !allowed.Contains(child.Key, StringComparer.OrdinalIgnoreCase))
        )
        {
            throw new InvalidOperationException(
                "Configuration contains an unsupported field or an unexpected scalar value."
            );
        }
    }

    internal static IConfigurationSection[] List(IConfigurationSection section)
    {
        // The .NET JSON provider represents [] as an empty string and {} / null as null.
        if (section.Value is not null and not "")
        {
            throw new InvalidOperationException("A configuration list must be an array.");
        }
        var children = section.GetChildren().ToArray();
        for (var index = 0; index < children.Length; index++)
        {
            if (children[index].Key != index.ToString(CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException(
                    "Configuration lists require consecutive array indices starting at zero."
                );
            }
        }
        return children;
    }

    internal static string Text(IConfigurationSection section)
    {
        var value = section.Value;
        if (
            string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Any(char.IsControl)
            || section.GetChildren().Any()
        )
        {
            throw new InvalidOperationException(
                "A configuration name or value must be nonempty, without surrounding whitespace or control characters."
            );
        }
        return value;
    }

    internal static bool Boolean(IConfigurationSection parent, string key, bool fallback)
    {
        var section = parent.GetSection(key);
        var declared = parent
            .GetChildren()
            .Any(child => child.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (section.GetChildren().Any() || (declared && !bool.TryParse(section.Value, out _)))
        {
            throw new InvalidOperationException("A configuration boolean must be true or false.");
        }
        return declared ? bool.Parse(section.Value!) : fallback;
    }

    internal static string PublicName(string name)
    {
        var suffix = name.StartsWith('#') ? name[1..] : name;
        if (string.IsNullOrWhiteSpace(suffix) || suffix.StartsWith('#') || suffix == "*")
        {
            throw new InvalidOperationException(
                "A public chat or scope name requires a name and at most one leading #."
            );
        }
        return "#" + suffix;
    }
}
