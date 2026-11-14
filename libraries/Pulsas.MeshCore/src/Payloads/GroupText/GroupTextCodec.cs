using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Funcky.Monads;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;

namespace Pulsas.MeshCore.Payloads.GroupText;

public static class GroupTextCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static Either<GroupTextEncodeError, byte[]> Encode(
        GroupTextMessage message,
        GroupChannelKey key
    )
    {
        key.ThrowIfDisposed();
        if (message.Attempt > 3)
        {
            return Either<GroupTextEncodeError, byte[]>.Left(
                new(GroupTextEncodeErrorKind.InvalidAttempt)
            );
        }

        if (message.Text.Contains('\0'))
        {
            return Either<GroupTextEncodeError, byte[]>.Left(
                new(GroupTextEncodeErrorKind.EmbeddedNul)
            );
        }

        int textLength;
        try
        {
            textLength = Utf8.GetByteCount(message.Text);
        }
        catch (EncoderFallbackException)
        {
            return Either<GroupTextEncodeError, byte[]>.Left(
                new(GroupTextEncodeErrorKind.InvalidText)
            );
        }
        if (textLength > 171)
        {
            return Either<GroupTextEncodeError, byte[]>.Left(
                new(GroupTextEncodeErrorKind.InvalidTextLength, textLength)
            );
        }

        var plaintext = new byte[(5 + textLength + 15) / 16 * 16];
        try
        {
            BinaryPrimitives.WriteUInt32LittleEndian(plaintext, message.Timestamp);
            plaintext[4] = message.Attempt;
            Utf8.GetBytes(message.Text, plaintext.AsSpan(5));
            var ciphertext = key.Transform(plaintext, encrypt: true);
            var mac = key.Mac(ciphertext);
            return Either<GroupTextEncodeError, byte[]>.Right([
                key.ChannelHash,
                mac[0],
                mac[1],
                .. ciphertext,
            ]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static Either<GroupTextDecodeError, GroupTextMessage> Decode(
        ReadOnlySpan<byte> payload,
        GroupChannelKey key
    )
    {
        key.ThrowIfDisposed();
        if (
            payload.Length is < 19 or > PacketCodec.MaximumPayloadLength
            || (payload.Length - 3) % 16 != 0
        )
        {
            return Either<GroupTextDecodeError, GroupTextMessage>.Left(
                new(GroupTextDecodeErrorKind.Malformed)
            );
        }

        if (payload[0] != key.ChannelHash)
        {
            return Either<GroupTextDecodeError, GroupTextMessage>.Left(
                new(GroupTextDecodeErrorKind.ChannelMismatch)
            );
        }

        var ciphertext = payload[3..];
        var mac = key.Mac(ciphertext);
        if (!CryptographicOperations.FixedTimeEquals(mac.AsSpan(0, 2), payload.Slice(1, 2)))
        {
            return Either<GroupTextDecodeError, GroupTextMessage>.Left(
                new(GroupTextDecodeErrorKind.MacMismatch)
            );
        }

        var plaintext = key.Transform(ciphertext, encrypt: false);
        try
        {
            if ((plaintext[4] >> 2) != 0)
            {
                return Either<GroupTextDecodeError, GroupTextMessage>.Left(
                    new(GroupTextDecodeErrorKind.UnsupportedTextType)
                );
            }

            var end = Array.IndexOf(plaintext, (byte)0, 5);
            if (end < 0)
            {
                end = plaintext.Length;
            }

            string text;
            try
            {
                text = Utf8.GetString(plaintext, 5, end - 5);
            }
            catch (DecoderFallbackException)
            {
                return Either<GroupTextDecodeError, GroupTextMessage>.Left(
                    new(GroupTextDecodeErrorKind.Malformed)
                );
            }
            return new GroupTextMessage(
                BinaryPrimitives.ReadUInt32LittleEndian(plaintext),
                text,
                plaintext[4]
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
