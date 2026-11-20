namespace Pulsas.PacketProcessing.GraphQL;

public record MeshPacketReceptionNotFoundError : IMeshPacketReceptionByIdResult
{
    public string Message => "No mesh packet reception has that ID.";
}
