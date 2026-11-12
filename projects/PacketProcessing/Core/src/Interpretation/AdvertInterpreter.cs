using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Processing;

namespace Pulsas.PacketProcessing.Core.Interpretation;

internal static class AdvertInterpreter
{
    public static PayloadInterpretation Interpret(MeshCorePacket packet)
    {
        if (!packet.IsSupportedVersion)
        {
            return new PayloadInterpretation.UnsupportedVersion();
        }
        if (packet.PacketType != PacketType.Advert)
        {
            return new PayloadInterpretation.Opaque();
        }
        if (!AdvertCodec.TryDecode(packet.Payload.AsSpan(), out var advert))
        {
            return new PayloadInterpretation.MalformedAdvert();
        }
        return new PayloadInterpretation.Advert(
            advert,
            AdvertCodec.Verify(advert)
                ? AdvertSignatureStatus.Verified
                : AdvertSignatureStatus.Invalid,
            AdvertAppDataCodec.Decode(advert.AppData.AsSpan())
        );
    }
}
