namespace Pulsas.Persistence.Packets;

public enum AdvertSignatureStatus
{
    NotChecked = 0,
    Malformed = 1,
    InvalidSignature = 2,
    Verified = 3,
}
