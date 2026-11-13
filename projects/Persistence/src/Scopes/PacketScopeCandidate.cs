namespace Pulsas.Persistence.Scopes;

// All current known-key matches, including the selected scope for a unique match.
public class PacketScopeCandidate
{
    public Guid PacketId { get; set; }
    public Guid ScopeId { get; set; }
}
