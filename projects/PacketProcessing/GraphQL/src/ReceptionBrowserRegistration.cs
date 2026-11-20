using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pulsas.PacketProcessing.GraphQL;

public static class ReceptionBrowserRegistration
{
    // Explicit query unions keep lookup fields nullable so execution errors preserve sibling data.
    public static IRequestExecutorBuilder AddReceptionBrowser(this IRequestExecutorBuilder graph) =>
        graph
            .AddQueryType<ReceptionQuery>()
            .AddReceptionBrowserTypes()
            .AddType<MeshPacket>()
            .AddType<MeshPacketPayload>()
            .AddType<MeshPacketReception>()
            .AddType<ObserverActivitySummary>()
            .AddType<MeshPacketNotFoundError>()
            .AddType<MeshPacketPayloadNotFoundError>()
            .AddType<MeshPacketReceptionNotFoundError>()
            .AddType<ObserverNotFoundError>()
            .AddType<PacketDecodeError>()
            .AddType<RequestPayload>()
            .AddType<ResponsePayload>()
            .AddType<TextPayload>()
            .AddType<AckPayload>()
            .AddType<AdvertPayload>()
            .AddType<DecodedAdvertPayload>()
            .AddType<DecodedGroupTextPayload>()
            .AddType<GroupTextPayload>()
            .AddType<GroupDataPayload>()
            .AddType<AnonymousRequestPayload>()
            .AddType<PathPayload>()
            .AddType<TracePayload>()
            .AddType<MultipartPayload>()
            .AddType<ControlPayload>()
            .AddType<Reserved12Payload>()
            .AddType<Reserved13Payload>()
            .AddType<Reserved14Payload>()
            .AddType<RawCustomPayload>();
}
