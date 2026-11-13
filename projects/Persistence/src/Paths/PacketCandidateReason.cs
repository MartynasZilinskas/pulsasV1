namespace Pulsas.Persistence.Paths;

public enum PacketCandidateReason
{
    Unknown = 0,
    PrefixMatch = 1,
    UniqueKnownPrefixMatch = 2,
    RegionMatch = 3,
    RegionMismatch = 4,
    NeighbourSupport = 5,
}
