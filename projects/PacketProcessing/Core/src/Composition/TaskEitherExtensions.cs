using Funcky.Monads;

namespace Pulsas.PacketProcessing.Core.Composition;

internal static class TaskEitherExtensions
{
    public static async Task<Either<TLeft, TResult>> SelectMany<TLeft, T, TResult>(
        this Task<Either<TLeft, T>> source,
        Func<T, Task<Either<TLeft, TResult>>> selector
    )
        where TLeft : notnull
        where T : notnull
        where TResult : notnull
    {
        var result = await source;
        return await result.Match(
            left: error => Task.FromResult(Either<TLeft, TResult>.Left(error)),
            right: selector
        );
    }

    public static async Task<Either<TLeft, T>> Inspect<TLeft, T>(
        this Task<Either<TLeft, T>> source,
        Action<T> action
    )
        where TLeft : notnull
        where T : notnull => (await source).Inspect(action);
}
