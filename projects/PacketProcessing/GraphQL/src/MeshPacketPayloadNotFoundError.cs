namespace Pulsas.PacketProcessing.GraphQL;

public record MeshPacketPayloadNotFoundError : IMeshPacketPayloadByIdResult
{
    public string Message => "No visible mesh packet payload has that ID.";
}
