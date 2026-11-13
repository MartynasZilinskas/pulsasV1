using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Pulsas.MeshCore.Cryptography;
using Pulsas.PacketProcessing.Contracts.Chat;

namespace Pulsas.Worker.ApiService.Configuration;

public class ConfiguredChannel
{
    internal ConfiguredChannel(
        ChannelKind kind,
        string name,
        string? configurationId,
        bool publication,
        ReadOnlySpan<byte> key
    )
    {
        Kind = kind;
        Name = name;
        ConfigurationId = configurationId;
        Public = publication;
        Key = [.. key];
    }

    public ChannelKind Kind { get; }
    public string Name { get; }
    public string? ConfigurationId { get; }
    public bool Public { get; }

    [JsonIgnore]
    internal ImmutableArray<byte> Key { get; }

    public GroupChannelKey CreateKey() => new(Key.AsSpan());
}
