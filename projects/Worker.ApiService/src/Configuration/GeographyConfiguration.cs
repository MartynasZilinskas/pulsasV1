using System.Collections.Immutable;
using System.Globalization;

namespace Pulsas.Worker.ApiService.Configuration;

internal static class GeographyConfiguration
{
    internal static ImmutableDictionary<string, ConfiguredRegion> ReadRegions(
        IConfiguration configuration
    )
    {
        var section = configuration.GetSection("regions");
        RequireMap(section);
        var regions = ImmutableDictionary.CreateBuilder<string, ConfiguredRegion>(
            StringComparer.Ordinal
        );
        foreach (var entry in section.GetChildren())
        {
            ConfigurationValues.Members(entry, "name", "country", "center", "border_file", "zoom");
            ValidateId(entry.Key);
            var country = ConfigurationValues.Text(entry.GetSection("country"));
            if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
            {
                throw new InvalidOperationException(
                    "Region country must be a two-uppercase-letter country code."
                );
            }
            var border = entry.GetSection("border_file");
            regions.Add(
                entry.Key,
                new(
                    entry.Key,
                    ConfigurationValues.Text(entry.GetSection("name")),
                    country,
                    ReadCenter(entry.GetSection("center"))
                        ?? throw new InvalidOperationException(
                            "Regions require a center [latitude, longitude]."
                        ),
                    border.Exists() ? ConfigurationValues.Text(border) : null,
                    ReadZoom(entry.GetSection("zoom"))
                )
            );
        }
        return regions.ToImmutable();
    }

    internal static ImmutableDictionary<string, ConfiguredRegionGroup> ReadGroups(
        IConfiguration configuration,
        ImmutableDictionary<string, ConfiguredRegion> regions
    )
    {
        var section = configuration.GetSection("region_groups");
        RequireMap(section);
        var groups = ImmutableDictionary.CreateBuilder<string, ConfiguredRegionGroup>(
            StringComparer.Ordinal
        );
        foreach (var entry in section.GetChildren())
        {
            ConfigurationValues.Members(entry, "name", "regions", "center", "zoom");
            ValidateId(entry.Key);
            var members = ConfigurationValues
                .List(entry.GetSection("regions"))
                .Select(ConfigurationValues.Text)
                .ToImmutableArray();
            if (
                members.IsEmpty
                || members.Distinct(StringComparer.Ordinal).Count() != members.Length
                || members.Any(id => !regions.ContainsKey(id))
            )
            {
                throw new InvalidOperationException(
                    "Region groups require unique references to existing regions."
                );
            }
            groups.Add(
                entry.Key,
                new(
                    entry.Key,
                    ConfigurationValues.Text(entry.GetSection("name")),
                    members,
                    ReadCenter(entry.GetSection("center")),
                    ReadZoom(entry.GetSection("zoom"))
                )
            );
        }
        return groups.ToImmutable();
    }

    internal static ConfiguredDefaultView? ReadDefault(
        IConfiguration configuration,
        ImmutableDictionary<string, ConfiguredRegion> regions,
        ImmutableDictionary<string, ConfiguredRegionGroup> groups
    )
    {
        var section = configuration.GetSection("default_view");
        ConfigurationValues.Members(section, "region", "region_group");
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            if (
                ((IConfigurationRoot)configuration).Providers.Any(provider =>
                    provider.TryGet("default_view", out _)
                )
            )
            {
                throw new InvalidOperationException(
                    "Default view must select exactly one region or region_group."
                );
            }
            return null;
        }
        if (children.Length != 1)
        {
            throw new InvalidOperationException(
                "Default view must select exactly one region or region_group."
            );
        }
        var id = ConfigurationValues.Text(children[0]);
        var region = children[0].Key.Equals("region", StringComparison.OrdinalIgnoreCase);
        if (region ? !regions.ContainsKey(id) : !groups.ContainsKey(id))
        {
            throw new InvalidOperationException(
                "Default view must reference an existing region or region group."
            );
        }
        return new(region ? id : null, region ? null : id);
    }

    private static void RequireMap(IConfigurationSection section)
    {
        if (section.Value is not null and not "")
        {
            throw new InvalidOperationException(
                "Regions and region_groups must be maps keyed by identifier."
            );
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || id.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Geographic identifiers must be nonempty, without surrounding whitespace or control characters."
            );
        }
    }

    private static ConfiguredCenter? ReadCenter(IConfigurationSection section)
    {
        var coordinates = ConfigurationValues.List(section);
        if (coordinates.Length == 0)
        {
            return null;
        }
        if (coordinates.Length != 2)
        {
            throw new InvalidOperationException("A center must contain [latitude, longitude].");
        }
        var latitude = Number(coordinates[0]);
        var longitude = Number(coordinates[1]);
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new InvalidOperationException(
                "Center coordinates are outside latitude/longitude bounds."
            );
        }
        return new(latitude, longitude);
    }

    private static double? ReadZoom(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }
        var zoom = Number(section);
        if (zoom < 0)
        {
            throw new InvalidOperationException("Map zoom must be nonnegative.");
        }
        return zoom;
    }

    private static double Number(IConfigurationSection section)
    {
        if (
            section.GetChildren().Any()
            || !double.TryParse(
                section.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value
            )
            || !double.IsFinite(value)
        )
        {
            throw new InvalidOperationException(
                "Geographic coordinates and zoom must be finite numbers."
            );
        }
        return value;
    }
}
