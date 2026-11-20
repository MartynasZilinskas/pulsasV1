using NodaTime;

namespace Pulsas.PacketProcessing.GraphQL;

public record ObserverActivityBucket(Instant From, Instant To, long ReceptionCount);
