using Funcky.Monads;
using Pulsas.MeshCore.Payloads.Adverts;

namespace Pulsas.PacketProcessing.Contracts.Processing;

public abstract record PayloadInterpretation
{
    public record UnsupportedVersion : PayloadInterpretation;

    // Retained as opaque content, not a claim that the protocol type is invalid.
    public record Opaque : PayloadInterpretation;

    public record MalformedAdvert : PayloadInterpretation;

    public record Advert(
        AdvertPayload Payload,
        AdvertSignatureStatus Signature,
        Either<AdvertAppDataDecodeError, Option<AdvertAppData>> AppData
    ) : PayloadInterpretation;
}
