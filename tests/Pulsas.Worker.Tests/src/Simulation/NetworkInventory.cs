using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pulsas.MeshCore.Nodes;

namespace Pulsas.Worker.Tests.Simulation;

public record NetworkInventory(ImmutableArray<NetworkNode> Nodes)
{
    public static NetworkInventory Load(string path) =>
        JsonSerializer.Deserialize<NetworkInventory>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                RespectRequiredConstructorParameters = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter<NodeRole>(allowIntegerValues: false) },
            }
        ) ?? throw new InvalidDataException("A network inventory object is required.");
}
