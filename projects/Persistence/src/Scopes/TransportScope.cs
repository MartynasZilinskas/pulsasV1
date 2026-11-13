namespace Pulsas.Persistence.Scopes;

// Routing scope, distinct from a geographic region or a chat channel. No secret keys are stored here.
public class TransportScope
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
}
