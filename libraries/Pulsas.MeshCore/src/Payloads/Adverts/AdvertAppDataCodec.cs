using System.Buffers.Binary;
using System.Text;
using Funcky.Monads;
using Pulsas.MeshCore.Nodes;

namespace Pulsas.MeshCore.Payloads.Adverts;

public static class AdvertAppDataCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    // Empty app data has no role or claims and is represented by null.
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out AdvertAppData? data)
    {
        var result = Decode(bytes)
            .Match(
                left: _ => (Success: false, Data: (AdvertAppData?)null),
                right: value =>
                    (
                        Success: true,
                        Data: value.Match<AdvertAppData?>(none: () => null, some: claims => claims)
                    )
            );
        data = result.Data;
        return result.Success;
    }

    public static Either<AdvertAppDataDecodeError, Option<AdvertAppData>> Decode(
        ReadOnlySpan<byte> bytes
    )
    {
        if (bytes.Length > AdvertCodec.MaximumAppDataLength)
        {
            return Either<AdvertAppDataDecodeError, Option<AdvertAppData>>.Left(
                new(AdvertAppDataDecodeErrorKind.Malformed)
            );
        }

        if (bytes.IsEmpty)
        {
            return Option<AdvertAppData>.None;
        }

        var flags = bytes[0];
        var required =
            1
            + ((flags & 0x10) != 0 ? 8 : 0)
            + ((flags & 0x20) != 0 ? 2 : 0)
            + ((flags & 0x40) != 0 ? 2 : 0);
        if (bytes.Length < required)
        {
            return Either<AdvertAppDataDecodeError, Option<AdvertAppData>>.Left(
                new(AdvertAppDataDecodeErrorKind.Malformed)
            );
        }

        int? latitude = null,
            longitude = null;
        ushort? feature1 = null,
            feature2 = null;
        var offset = 1;
        if ((flags & 0x10) != 0)
        {
            latitude = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            longitude = BinaryPrimitives.ReadInt32LittleEndian(bytes[(offset + 4)..]);
            offset += 8;
        }
        if ((flags & 0x20) != 0)
        {
            feature1 = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            offset += 2;
        }
        if ((flags & 0x40) != 0)
        {
            feature2 = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            offset += 2;
        }
        var data = new AdvertAppData(
            (NodeRole)(flags & 15),
            latitude,
            longitude,
            feature1,
            feature2
        );
        if ((flags & 0x80) != 0)
        {
            try
            {
                data = data with { Name = Utf8.GetString(bytes[offset..]) };
            }
            catch (DecoderFallbackException)
            {
                return Either<AdvertAppDataDecodeError, Option<AdvertAppData>>.Left(
                    new(AdvertAppDataDecodeErrorKind.Malformed, data)
                );
            }
        }
        else if (offset != bytes.Length)
        {
            return Either<AdvertAppDataDecodeError, Option<AdvertAppData>>.Left(
                new(AdvertAppDataDecodeErrorKind.Unsupported, data)
            );
        }

        return Option.Some(data);
    }

    public static Either<AdvertAppDataEncodeError, byte[]> Encode(AdvertAppData? data)
    {
        if (data is null)
        {
            return Either<AdvertAppDataEncodeError, byte[]>.Right([]);
        }

        if (data.RoleCode > 15)
        {
            return Either<AdvertAppDataEncodeError, byte[]>.Left(
                new(AdvertAppDataEncodeErrorKind.InvalidRole)
            );
        }

        if (data.LatitudeMicrodegrees.HasValue != data.LongitudeMicrodegrees.HasValue)
        {
            return Either<AdvertAppDataEncodeError, byte[]>.Left(
                new(AdvertAppDataEncodeErrorKind.IncompleteCoordinates)
            );
        }

        byte[] name;
        try
        {
            name = data.Name is null ? [] : Utf8.GetBytes(data.Name);
        }
        catch (EncoderFallbackException)
        {
            return Either<AdvertAppDataEncodeError, byte[]>.Left(
                new(AdvertAppDataEncodeErrorKind.InvalidName)
            );
        }

        var length =
            1
            + (data.LatitudeMicrodegrees.HasValue ? 8 : 0)
            + (data.Feature1.HasValue ? 2 : 0)
            + (data.Feature2.HasValue ? 2 : 0)
            + name.Length;
        if (length > AdvertCodec.MaximumAppDataLength)
        {
            return Either<AdvertAppDataEncodeError, byte[]>.Left(
                new(AdvertAppDataEncodeErrorKind.InvalidLength, length)
            );
        }

        var bytes = new byte[length];
        bytes[0] = (byte)(
            data.RoleCode
            | (data.LatitudeMicrodegrees.HasValue ? 0x10 : 0)
            | (data.Feature1.HasValue ? 0x20 : 0)
            | (data.Feature2.HasValue ? 0x40 : 0)
            | (data.Name is not null ? 0x80 : 0)
        );
        var offset = 1;
        if (data.LatitudeMicrodegrees.HasValue)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(offset),
                data.LatitudeMicrodegrees.Value
            );
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(offset + 4),
                data.LongitudeMicrodegrees!.Value
            );
            offset += 8;
        }
        if (data.Feature1.HasValue)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), data.Feature1.Value);
            offset += 2;
        }
        if (data.Feature2.HasValue)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), data.Feature2.Value);
            offset += 2;
        }
        name.CopyTo(bytes, offset);
        return bytes;
    }
}
