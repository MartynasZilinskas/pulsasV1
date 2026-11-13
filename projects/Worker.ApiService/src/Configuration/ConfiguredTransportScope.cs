using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Pulsas.Worker.ApiService.Configuration;

public class ConfiguredTransportScope
{
    internal ConfiguredTransportScope(string name, ReadOnlySpan<byte> key)
    {
        Name = name;
        Key = [.. key];
    }

    public string Name { get; }

    [JsonIgnore]
    internal ImmutableArray<byte> Key { get; }
}
