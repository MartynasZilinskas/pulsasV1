namespace Pulsas.PacketProcessing.GraphQL;

[GraphQLName("NodeRole")]
public enum LegacyNodeRole : byte
{
    None = 0,
    Client,
    Repeater,
    RoomServer,
    Sensor,

    [GraphQLName("RESERVED5")]
    Reserved5,

    [GraphQLName("RESERVED6")]
    Reserved6,

    [GraphQLName("RESERVED7")]
    Reserved7,

    [GraphQLName("RESERVED8")]
    Reserved8,

    [GraphQLName("RESERVED9")]
    Reserved9,

    [GraphQLName("RESERVED10")]
    Reserved10,

    [GraphQLName("RESERVED11")]
    Reserved11,

    [GraphQLName("RESERVED12")]
    Reserved12,

    [GraphQLName("RESERVED13")]
    Reserved13,

    [GraphQLName("RESERVED14")]
    Reserved14,

    [GraphQLName("RESERVED15")]
    Reserved15,
}
