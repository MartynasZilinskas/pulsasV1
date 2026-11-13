namespace Pulsas.Persistence.Scopes;

public enum ScopeResolutionStatus
{
    NotAssessed = 0,
    Unscoped = 1,
    Wildcard = 2,
    Matched = 3,
    Unresolved = 4,
    Ambiguous = 5,
    Share = 6,
}
