namespace Pulsas.Persistence.Receptions;

public enum PacketAssociation
{
    Unverified = 0,
    ConfirmedFixture = 1,

    // Operator-accepted observer/packet association for offline test data, not a live-feed override.
    OfflineImport = 2,

    // Operator-accepted publisher assertion; raw may be captured, cached or reconstructed.
    PublisherAssertedRx = 3,
}
