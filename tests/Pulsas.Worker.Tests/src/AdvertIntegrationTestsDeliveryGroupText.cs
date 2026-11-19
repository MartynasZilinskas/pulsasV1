using Funcky;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NodaTime;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.GroupText;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.PacketProcessing.Core.Chat;
using Pulsas.Persistence.Chat;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService.Configuration;
using Pulsas.Worker.ApiService.Delivery;
using Xunit;
using static Pulsas.PacketProcessing.Contracts.Chat.IGroupTextManager;

namespace Pulsas.Worker.Tests;

public partial class AdvertIntegrationTestsDelivery
{
    private static PacketConfiguration HashtagConfiguration(string name = "#test") =>
        PacketConfiguration.Read(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { ["chat:hashtags:0"] = name }
                )
                .Build()
        );

    [GeographyFact]
    public async Task Queue_projects_group_text_and_canonicalizes_attempts_and_paths()
    {
        var configuration = HashtagConfiguration();
        var channel = Assert.Single(configuration.Channels);
        var channelId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        at = at.AddTicks(-(at.Ticks % 10));
        var text = "[claimed] hello #verbatim";

        await using (var setup = new DeliveryDatabaseFactory().CreateDbContext())
        {
            setup.Channels.Add(
                new Channel
                {
                    Id = channelId,
                    Kind = channel.Kind,
                    ConfigurationId = channel.ConfigurationId,
                    Name = channel.Name,
                    Active = true,
                    Public = channel.Public,
                }
            );
            await setup.SaveChangesAsync();
        }

        try
        {
            using var key = channel.CreateKey();
            await using (var scenario = new ReportScenario(4))
            {
                var ids = scenario.Receptions;
                var timestamps = new[] { 1u, 1u, 2u, 1u };
                var attempts = new byte[] { 0, 3, 1, 2 };
                for (var index = 0; index < ids.Length; index++)
                {
                    var payload = Assert.IsType<byte[]>(
                        GroupTextCodec
                            .Encode(new(timestamps[index], text, attempts[index]), key)
                            .Match<object>(left: error => error, right: value => value)
                    );
                    var raw = Convert.ToHexStringLower(
                        PacketCodec.Encode(
                            PacketCodec.Create(RouteType.Flood, PacketType.GroupText, payload)
                        )
                    );
                    await scenario.ReceiveAsync(
                        settings,
                        ObserverReports.Packet(scenario.Observer, raw, at),
                        at,
                        index
                    );
                    Assert.True(await Process(ReportQueues.Ingestion));
                }

                Assert.True(
                    await Process(
                        ReportQueues.Content,
                        packetConfiguration: configuration,
                        groupText: database => new FailingGroupTextAfterWrite(
                            new GroupTextManager(
                                database,
                                configuration.Channels,
                                SystemClock.Instance
                            )
                        )
                    )
                );
                await using (var rolledBack = new DeliveryDatabaseFactory().CreateDbContext())
                {
                    Assert.False(
                        await rolledBack.ChatMessages.AnyAsync(row => row.ChannelId == channelId)
                    );
                    Assert.False(
                        await rolledBack
                            .MeshPacketReceptions.Where(row => ids.Contains(row.Id))
                            .AnyAsync(row => row.Packet!.ChatMessageId != null)
                    );
                }

                for (var index = 0; index < ids.Length; index++)
                {
                    Assert.True(
                        await Process(ReportQueues.Content, packetConfiguration: configuration)
                    );
                }
                Assert.False(
                    await Process(ReportQueues.Content, packetConfiguration: configuration)
                );

                await using var check = new DeliveryDatabaseFactory().CreateDbContext();
                var messages = await check
                    .ChatMessages.Where(row => row.ChannelId == channelId)
                    .ToListAsync();
                Assert.Equal(2, messages.Count);
                Assert.All(messages, message =>
                {
                    Assert.Equal(text, message.Text);
                    Assert.Null(message.ClaimedSenderName);
                });
                Assert.Equal(
                    Instant.FromUnixTimeSeconds(1),
                    messages
                        .Single(message => message.SentAt == Instant.FromUnixTimeSeconds(1))
                        .SentAt
                );
                var receptions = await check
                    .MeshPacketReceptions.Where(row => ids.Contains(row.Id))
                    .Include(row => row.Packet)
                    .ToListAsync();
                Assert.Equal(ids.Length, receptions.Count);
                receptions = ids.Select(id => receptions.Single(row => row.Id == id)).ToList();
                Assert.All(receptions, reception =>
                    Assert.NotNull(reception.Packet!.ChatMessageId)
                );
                Assert.Equal(
                    messages.Single(message => message.SentAt == Instant.FromUnixTimeSeconds(1)).Id,
                    receptions[0].Packet!.ChatMessageId
                );
                Assert.Equal(
                    receptions[0].Packet!.ChatMessageId,
                    receptions[1].Packet!.ChatMessageId
                );
                Assert.NotEqual(
                    receptions[0].Packet!.ChatMessageId,
                    receptions[2].Packet!.ChatMessageId
                );
                Assert.Equal(
                    receptions[0].Packet!.ChatMessageId,
                    receptions[3].Packet!.ChatMessageId
                );
            }
        }
        finally
        {
            await using var cleanup = new DeliveryDatabaseFactory().CreateDbContext();
            var messageIds = await cleanup
                .ChatMessages.Where(row => row.ChannelId == channelId)
                .Select(row => row.Id)
                .ToListAsync();
            await cleanup
                .MeshPackets.Where(packet =>
                    packet.ChatMessageId != null && messageIds.Contains(packet.ChatMessageId.Value)
                )
                .ExecuteUpdateAsync(update =>
                    update.SetProperty(packet => packet.ChatMessageId, (Guid?)null)
                );
            await cleanup
                .ChatMessages.Where(row => row.ChannelId == channelId)
                .ExecuteDeleteAsync();
            await cleanup.Channels.Where(row => row.Id == channelId).ExecuteDeleteAsync();
        }
    }

    private class FailingGroupTextAfterWrite(IGroupTextManager inner) : IGroupTextManager
    {
        public async Task<Either<ProcessGroupTextError, Unit>> ProcessGroupTextAsync(
            ProcessGroupTextInput input,
            CancellationToken cancellationToken
        )
        {
            await inner.ProcessGroupTextAsync(input, cancellationToken);
            throw new InvalidOperationException("Synthetic failure after chat projection writes.");
        }
    }
}
