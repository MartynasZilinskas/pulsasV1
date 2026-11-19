using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Funcky;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.GroupText;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.Persistence;
using Pulsas.Persistence.Chat;
using static Pulsas.PacketProcessing.Contracts.Chat.IGroupTextManager;

namespace Pulsas.PacketProcessing.Core.Chat;

public class GroupTextManager(
    TrackingDbContext database,
    ImmutableArray<ConfiguredChannel> channels,
    IClock clock
) : IGroupTextManager
{
    public async Task<Either<ProcessGroupTextError, Unit>> ProcessGroupTextAsync(
        ProcessGroupTextInput input,
        CancellationToken cancellationToken
    )
    {
        var packet = await database
            .MeshPackets.Include(item => item.Content)
            .SingleOrDefaultAsync(item => item.Id == input.PacketId, cancellationToken);
        if (packet is null)
        {
            return Either<ProcessGroupTextError, Unit>.Left(ProcessGroupTextError.PacketNotFound);
        }
        if (packet.Type != PacketType.GroupText || packet.Content.PayloadVersion != 0)
        {
            return Unit.Value;
        }

        var decoded = new List<(ConfiguredChannel Channel, GroupTextMessage Message)>();
        foreach (var channel in channels)
        {
            using var key = channel.CreateKey();
            GroupTextCodec
                .Decode(packet.Content.Payload, key)
                .Switch(left: _ => { }, right: message => decoded.Add((channel, message)));
        }
        // Multiple successful descriptors are ambiguous, even if they share a key.
        if (decoded.Count != 1)
        {
            return Unit.Value;
        }

        var (configured, text) = decoded[0];
        var possibleChannels = await database
            .Channels.Where(row => row.Kind == configured.Kind)
            .ToListAsync(cancellationToken);
        var channelRow = possibleChannels.SingleOrDefault(row =>
            configured.Kind switch
            {
                ChannelKind.StandardPublic => true,
                ChannelKind.Hashtag => row.Name == configured.Name,
                ChannelKind.ExplicitKey => string.Equals(
                    row.ConfigurationId,
                    configured.ConfigurationId,
                    StringComparison.OrdinalIgnoreCase
                ),
                _ => false,
            }
        );
        if (
            channelRow is not { Active: true }
            || channelRow.Public != configured.Public
            || channelRow.Name != configured.Name
        )
        {
            return Either<ProcessGroupTextError, Unit>.Left(ProcessGroupTextError.MissingChannel);
        }

        var hashInput = new byte[4 + Encoding.UTF8.GetByteCount(text.Text)];
        BinaryPrimitives.WriteUInt32LittleEndian(hashInput, text.Timestamp);
        Encoding.UTF8.GetBytes(text.Text, hashInput.AsSpan(4));
        var canonicalHash = SHA256.HashData(hashInput);
        CryptographicOperations.ZeroMemory(hashInput);

        var existing = await database.ChatMessages.SingleOrDefaultAsync(
            message => message.ChannelId == channelRow.Id && message.PayloadHash == canonicalHash,
            cancellationToken
        );
        var messageId = existing?.Id ?? GuidV7.Create(clock);
        if (packet.ChatMessageId is Guid linkedId && linkedId != messageId)
        {
            return Either<ProcessGroupTextError, Unit>.Left(
                ProcessGroupTextError.ConflictingPacketLink
            );
        }

        if (existing is null)
        {
            database.ChatMessages.Add(
                new ChatMessage
                {
                    Id = messageId,
                    ChannelId = channelRow.Id,
                    Channel = channelRow,
                    PayloadHash = canonicalHash,
                    ClaimedSenderName = null,
                    Text = text.Text,
                    SentAt = Instant.FromUnixTimeSeconds(text.Timestamp),
                }
            );
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (PostgresWriteErrors.PublicUniqueViolation(exception)
                        is ("chat_messages", "IX_chat_messages_ChannelId_PayloadHash")
                )
            {
                return Either<ProcessGroupTextError, Unit>.Left(
                    ProcessGroupTextError.ConcurrentCreation
                );
            }
        }

        var updated = await database
            .MeshPackets.Where(row =>
                row.Id == input.PacketId
                && (row.ChatMessageId == null || row.ChatMessageId == messageId)
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ChatMessageId, messageId),
                cancellationToken
            );
        if (updated != 1)
        {
            return Either<ProcessGroupTextError, Unit>.Left(
                ProcessGroupTextError.ConflictingPacketLink
            );
        }
        packet.ChatMessageId = messageId;
        packet.ChatMessage = existing
            ?? database.ChatMessages.Local.Single(message => message.Id == messageId);
        return Unit.Value;
    }
}
