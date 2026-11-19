using System.Collections.Immutable;
using Pulsas.PacketProcessing.Core.Chat;

namespace Pulsas.Worker.ApiService.Configuration;

// Immutable startup snapshot for channel reconciliation and content processing.
public class PacketConfiguration
{
    private PacketConfiguration(
        ImmutableArray<ConfiguredChannel> channels,
        ImmutableArray<ConfiguredTransportScope> scopes,
        ImmutableDictionary<string, ConfiguredRegion> regions,
        ImmutableDictionary<string, ConfiguredRegionGroup> regionGroups,
        ConfiguredDefaultView? defaultView
    )
    {
        Channels = channels;
        Scopes = scopes;
        Regions = regions;
        RegionGroups = regionGroups;
        DefaultView = defaultView;
    }

    public ImmutableArray<ConfiguredChannel> Channels { get; }
    public ImmutableArray<ConfiguredTransportScope> Scopes { get; }
    public ImmutableDictionary<string, ConfiguredRegion> Regions { get; }
    public ImmutableDictionary<string, ConfiguredRegionGroup> RegionGroups { get; }
    public ConfiguredDefaultView? DefaultView { get; }

    public static PacketConfiguration Read(IConfigurationRoot configuration)
    {
        using var snapshot = ConfigurationSnapshot.Read(configuration);
        var regions = GeographyConfiguration.ReadRegions(snapshot);
        var groups = GeographyConfiguration.ReadGroups(snapshot, regions);
        return new(
            ChatConfiguration.Read(snapshot),
            ScopeConfiguration.Read(snapshot),
            regions,
            groups,
            GeographyConfiguration.ReadDefault(snapshot, regions, groups)
        );
    }
}
