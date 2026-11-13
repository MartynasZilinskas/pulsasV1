using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Payloads.GroupText;
using Xunit;

namespace Pulsas.MeshCore.Tests.Payloads.GroupText;

public class GroupTextTests
{
    // Independent OpenSSL AES-128-ECB plus Python hashlib/hmac vectors; see README.
    [Theory]
    [InlineData(
        16,
        "Alice: hello",
        0,
        "be413968cfaf2d14654881b81569d3767aae186c0bdf9ac27cd83cc7396849eea7bfc2"
    )]
    [InlineData(
        32,
        "Alice: hello",
        0,
        "63c65f68cfaf2d14654881b81569d3767aae186c0bdf9ac27cd83cc7396849eea7bfc2"
    )]
    [InlineData(16, "12345678901", 3, "be833552be2422f92e7c48498c054a2433a330")]
    [InlineData(32, "Žinutė", 1, "63e5ef8120e3d117f39c53f5a4d4b1959dadee")]
    public void Matches_independent_vectors_in_both_directions(
        int size,
        string text,
        byte attempt,
        string hex
    )
    {
        using var key = new GroupChannelKey(
            Enumerable.Range(0, size).Select(i => (byte)i).ToArray()
        );
        var message = new GroupTextMessage(1_700_000_000, text, attempt);
        Assert.Equal(hex, Convert.ToHexStringLower(GroupTextCodec.Encode(message, key)));
        var decoded = GroupTextCodec.Decode(Convert.FromHexString(hex), key);
        Assert.Equal(GroupTextDecodeStatus.Success, decoded.Status);
        Assert.Equal(message, decoded.Message);
    }

    [Fact]
    public void Retains_attempt_bits_empty_text_and_maximum_UTF8_byte_lengths()
    {
        using var key = GroupChannelKey.StandardPublic();
        foreach (var text in new[] { "", new string('x', 171), new string('Ž', 85) + "x" })
        {
            foreach (byte attempt in new byte[] { 0, 1, 2, 3 })
            {
                var message = new GroupTextMessage(uint.MaxValue, text, attempt);
                Assert.Equal(
                    message,
                    GroupTextCodec.Decode(GroupTextCodec.Encode(message, key), key).Message
                );
            }
        }

        Assert.Throws<ArgumentException>(() =>
            GroupTextCodec.Encode(new(0, new string('x', 172)), key)
        );
        Assert.Throws<ArgumentException>(() =>
            GroupTextCodec.Encode(new(0, new string('Ž', 86)), key)
        );
        Assert.Throws<ArgumentException>(() => GroupTextCodec.Encode(new(0, "a\0b"), key));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupTextCodec.Encode(new(0, "", 4), key));
        Assert.Throws<EncoderFallbackException>(() => GroupTextCodec.Encode(new(0, "\ud800"), key));
    }

    [Fact]
    public void Separates_malformed_channel_mismatch_MAC_failure_and_unsupported_text()
    {
        byte[] secret = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];
        using var key = new GroupChannelKey(secret);
        var payload = GroupTextCodec.Encode(new(0, "hello"), key);
        var otherChannel = payload.ToArray();
        otherChannel[0] ^= 1;
        Assert.Equal(
            GroupTextDecodeStatus.ChannelMismatch,
            GroupTextCodec.Decode(otherChannel, key).Status
        );
        payload[1] ^= 1;
        Assert.Equal(GroupTextDecodeStatus.MacMismatch, GroupTextCodec.Decode(payload, key).Status);
        payload[^1] ^= 1;
        Assert.Equal(GroupTextDecodeStatus.MacMismatch, GroupTextCodec.Decode(payload, key).Status);
        foreach (var length in new[] { 0, 3, 4, 18, 20, 195 })
        {
            Assert.Equal(
                GroupTextDecodeStatus.Malformed,
                GroupTextCodec.Decode(new byte[length], key).Status
            );
        }

        Assert.Equal(
            GroupTextDecodeStatus.UnsupportedTextType,
            GroupTextCodec.Decode(InvalidTextPayload(secret, 4, 0xff), key).Status
        );
        Assert.Equal(
            GroupTextDecodeStatus.Malformed,
            GroupTextCodec.Decode(InvalidTextPayload(secret, 0, 0xff), key).Status
        );
    }

    [Fact]
    public void Key_ownership_is_defensive_disposable_and_nonserializing()
    {
        byte[] secret = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];
        using var key = new GroupChannelKey(secret);
        var expected = GroupTextCodec.Encode(new(0, "hello"), key);
        Array.Fill(secret, (byte)0xff);
        Assert.Equal(expected, GroupTextCodec.Encode(new(0, "hello"), key));
        // The span-only secret constructor is intentionally not a JSON contract.
        // System.Text.Json rejects it rather than serializing any secret bytes.
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(key));
        key.Dispose();
        Assert.Throws<ObjectDisposedException>(() => GroupTextCodec.Encode(new(0, "hello"), key));
        Assert.Throws<ObjectDisposedException>(() => GroupTextCodec.Decode([], key));
        key.Dispose();
        Assert.Throws<ArgumentException>(() => new GroupChannelKey(new byte[15]));
        Assert.Throws<ArgumentException>(() => new GroupChannelKey(new byte[33]));
    }

    [Fact]
    public void Channel_helpers_match_frozen_OpenSSL_vectors()
    {
        // Frozen independently with OpenSSL AES and Python hashlib/hmac.
        using var standard = GroupChannelKey.StandardPublic();
        Assert.Equal(
            "115246432e02124c1477aaa2fd962c103d8a1f",
            Convert.ToHexStringLower(GroupTextCodec.Encode(new(0, ""), standard))
        );
        using var hashtag = GroupChannelKey.FromHashtag("#Test ");
        Assert.Equal(
            "0434817f4304cc4fdd0258a3bef93bfff669df",
            Convert.ToHexStringLower(GroupTextCodec.Encode(new(0, ""), hashtag))
        );
        Assert.Throws<ArgumentException>(() => GroupChannelKey.FromHashtag("test"));
    }

    private static byte[] InvalidTextPayload(byte[] key, byte flags, byte text)
    {
        byte[] plaintext = [0, 0, 0, 0, flags, text, .. new byte[10]];
        using var aes = Aes.Create();
        aes.Key = key;
        var ciphertext = aes.EncryptEcb(plaintext, PaddingMode.None);
        byte[] paddedKey = [.. key, .. new byte[16]];
        var mac = HMACSHA256.HashData(paddedKey, ciphertext);
        return [SHA256.HashData(key)[0], mac[0], mac[1], .. ciphertext];
    }
}
