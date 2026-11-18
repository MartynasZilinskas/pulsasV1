using Funcky.Monads;

namespace Pulsas.PacketProcessing.Core.Composition;

internal static class TaskOptionExtensions
{
    public static async Task<TResult> Match<T, TResult>(
        this Task<Option<T>> source,
        Func<Task<TResult>> none,
        Func<T, Task<TResult>> some
    )
        where T : notnull
    {
        var option = await source;
        return await option.Match(none: none, some: some);
    }

    public static async Task<Option<TResult>> SelectMany<T, TResult>(
        this Task<Option<T>> source,
        Func<T, Option<TResult>> selector
    )
        where T : notnull
        where TResult : notnull => (await source).SelectMany(selector);

    public static async Task<Either<TLeft, T>> ToEither<TLeft, T>(
        this Task<Option<T>> source,
        TLeft error
    )
        where TLeft : notnull
        where T : notnull => (await source).ToEither(error);
}
