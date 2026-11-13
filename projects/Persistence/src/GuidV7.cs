using NodaTime;

namespace Pulsas.Persistence;

public static class GuidV7
{
    public static Guid Create(IClock clock) =>
        Guid.CreateVersion7(clock.GetCurrentInstant().ToDateTimeOffset());
}
