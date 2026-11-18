using Funcky.Monads;
using Pulsas.PacketProcessing.Core.Composition;
using Xunit;

namespace Pulsas.PacketProcessing.Tests;

public class AsyncCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Synchronous_filter_remains_available_with_async_extension_in_scope(bool matches)
    {
        Option<int> result = Option.Some(7).Where(_ => matches);

        Assert.Equal(matches ? Option.Some(7) : Option<int>.None, result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Async_filter_preserves_only_present_matching_values(
        bool present,
        bool matches
    )
    {
        var calls = 0;
        var result = await (present ? Option.Some(7) : Option<int>.None).Where(value =>
        {
            calls++;
            Assert.Equal(7, value);
            return Task.FromResult(matches);
        });

        Assert.Equal(present ? 1 : 0, calls);
        Assert.Equal(present && matches ? Option.Some(7) : Option<int>.None, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_filter_awaits_predicate_before_dispatch(bool matches)
    {
        var predicate = new TaskCompletionSource<bool>();
        var someCalls = 0;
        var noneCalls = 0;
        var pending = Option
            .Some(7)
            .Where(_ => predicate.Task)
            .Match(
                none: () =>
                {
                    noneCalls++;
                    return Task.FromResult(0);
                },
                some: value =>
                {
                    someCalls++;
                    return Task.FromResult(value);
                }
            );

        Assert.False(pending.IsCompleted);
        Assert.Equal(0, someCalls + noneCalls);
        predicate.SetResult(matches);
        Assert.Equal(matches ? 7 : 0, await pending);
        Assert.Equal(matches ? 1 : 0, someCalls);
        Assert.Equal(matches ? 0 : 1, noneCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_filter_propagates_predicate_failures(bool synchronous)
    {
        var cause = new InvalidOperationException("synthetic");
        var pending = Option
            .Some(7)
            .Where(_ => synchronous ? throw cause : Task.FromException<bool>(cause));

        Assert.Same(cause, await Record.ExceptionAsync(() => pending));
    }

    [Fact]
    public async Task Async_filter_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var pending = Option
            .Some(7)
            .Where(_ => Task.FromCanceled<bool>(cancellation.Token))
            .Match(
                none: () => Task.FromException<int>(new InvalidOperationException("Must not run.")),
                some: _ => Task.FromException<int>(new InvalidOperationException("Must not run."))
            );

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(pending.IsCanceled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Optional_pipeline_skips_callbacks_when_absent(bool present)
    {
        var projected = 0;
        var inspected = 0;
        var bound = 0;
        var result = await Task.FromResult(present ? Option.Some(2) : Option<int>.None)
            .SelectMany(value =>
            {
                projected++;
                return Option.Some(value * 3);
            })
            .ToEither("missing")
            .Inspect(_ => inspected++)
            .SelectMany(value =>
            {
                bound++;
                return Task.FromResult(Either<string, int>.Right(value + 1));
            });

        Assert.Equal(present ? 1 : 0, projected);
        Assert.Equal(present ? 1 : 0, inspected);
        Assert.Equal(present ? 1 : 0, bound);
        Assert.Equal(
            present ? "7" : "missing",
            result.Match(left: error => error, right: value => value.ToString())
        );
    }

    [Fact]
    public async Task Async_bind_preserves_returned_failure_and_skips_later_effects()
    {
        var cause = new Exception("synthetic");
        var result = await Task.FromResult(Either<Exception, int>.Right(1))
            .SelectMany(_ => Task.FromResult(Either<Exception, int>.Left(cause)))
            .Inspect(_ => throw new InvalidOperationException("Must not run."))
            .SelectMany(
                (int _) =>
                    Task.FromException<Either<Exception, int>>(
                        new InvalidOperationException("Must not run.")
                    )
            );

        Assert.Same(
            cause,
            result.Match(
                left: error => error,
                right: _ => throw new InvalidOperationException("Expected failure.")
            )
        );
    }

    [Fact]
    public async Task Async_bind_awaits_source_and_callback()
    {
        var source = new TaskCompletionSource<Either<string, int>>();
        var callback = new TaskCompletionSource<Either<string, int>>();
        var calls = 0;
        var pending = source.Task.SelectMany(value =>
        {
            Assert.Equal(2, value);
            calls++;
            return callback.Task;
        });

        Assert.Equal(0, calls);
        Assert.False(pending.IsCompleted);
        source.SetResult(Either<string, int>.Right(2));
        Assert.Equal(1, calls);
        Assert.False(pending.IsCompleted);
        callback.SetResult(Either<string, int>.Right(3));
        Assert.Equal(
            3,
            (await pending).GetOrElse(_ => throw new InvalidOperationException("Expected success."))
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_bind_propagates_faults_and_cancellation(bool fromSource)
    {
        var cause = new InvalidOperationException("synthetic");
        var pending = (
            fromSource
                ? Task.FromException<Either<string, int>>(cause)
                : Task.FromResult(Either<string, int>.Right(1))
        ).SelectMany(_ => Task.FromException<Either<string, int>>(cause));
        Assert.Same(cause, await Record.ExceptionAsync(() => pending));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = (
            fromSource
                ? Task.FromCanceled<Either<string, int>>(cancellation.Token)
                : Task.FromResult(Either<string, int>.Right(1))
        ).SelectMany(_ => Task.FromCanceled<Either<string, int>>(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(canceled.IsCanceled);
    }
}
