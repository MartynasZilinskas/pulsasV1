using Funcky.Monads;

namespace Pulsas.PacketProcessing.Core.Composition;

internal static class OptionAsyncExtensions
{
    public static Task<Option<T>> Where<T>(this Option<T> source, Func<T, Task<bool>> predicate)
        where T : notnull =>
        source.Match(
            none: () => Task.FromResult(Option<T>.None),
            some: async value => await predicate(value) ? source : Option<T>.None
        );
}
