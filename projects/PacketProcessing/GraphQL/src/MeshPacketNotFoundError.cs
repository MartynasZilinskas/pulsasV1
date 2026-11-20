namespace Pulsas.PacketProcessing.GraphQL;

public record MeshPacketNotFoundError : IMeshPacketByIdResult
{
    public string Message => "No visible mesh packet has that ID.";
}
