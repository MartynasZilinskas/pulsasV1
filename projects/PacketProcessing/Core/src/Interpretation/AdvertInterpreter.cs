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
        return AdvertCodec
            .Decode(packet.Payload.AsSpan())
            .Match<PayloadInterpretation>(
                left: _ => new PayloadInterpretation.MalformedAdvert(),
                right: advert => new PayloadInterpretation.Advert(
                    advert,
                    AdvertCodec
                        .ValidateSignature(advert)
                        .Match(
                            left: _ => AdvertSignatureStatus.Invalid,
                            right: _ => AdvertSignatureStatus.Verified
                        ),
                    AdvertAppDataCodec.Decode(advert.AppData.AsSpan())
                )
            );
    }
}
