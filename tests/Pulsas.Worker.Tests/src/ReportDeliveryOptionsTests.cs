using Pulsas.Worker.ApiService.Delivery;
using Xunit;

namespace Pulsas.Worker.Tests;

public class ReportDeliveryOptionsTests
{
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(-1, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 0, -1)]
    public void Rejects_invalid_delivery_limits(int attempts, int retryDelay, int pollDelay) =>
        Assert.False(
            new ReportDeliveryOptions
            {
                MaximumAttempts = attempts,
                RetryDelaySeconds = retryDelay,
                PollDelayMilliseconds = pollDelay,
            }.IsValid()
        );

    [Theory]
    [InlineData("same", "same", "failed")]
    [InlineData("same", "content", "same")]
    [InlineData("ingestion", "same", "same")]
    [InlineData("", "content", "failed")]
    [InlineData("bad-name", "content", "failed")]
    [InlineData("BAD", "content", "failed")]
    public void Rejects_ambiguous_or_invalid_queue_roles(
        string ingestion,
        string content,
        string quarantine
    ) =>
        Assert.False(
            new ReportDeliveryOptions
            {
                IngestionQueue = ingestion,
                ContentQueue = content,
                QuarantineQueue = quarantine,
            }.IsValid()
        );

    [Fact]
    public void Defaults_and_distinct_isolated_names_are_valid()
    {
        Assert.Equal(4, new ReportDeliveryOptions().ContentConcurrency);
        Assert.True(new ReportDeliveryOptions().IsValid());
        Assert.True(
            new ReportDeliveryOptions
            {
                IngestionQueue = "test_in_123",
                ContentQueue = "test_content_123",
                QuarantineQueue = "test_failed_123",
            }.IsValid()
        );
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(16, true)]
    public void Validates_content_concurrency(int concurrency, bool valid) =>
        Assert.Equal(
            valid,
            new ReportDeliveryOptions { ContentConcurrency = concurrency }.IsValid()
        );
}
