using System.Security.Cryptography;
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
        Assert.Equal(hex, Convert.ToHexStringLower(EncodeSuccess(message, key)));
        var decoded = GroupTextCodec.Decode(Convert.FromHexString(hex), key);
        Assert.Equal(message, decoded.Match<object>(left: error => error, right: value => value));
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
                    GroupTextCodec
                        .Decode(EncodeSuccess(message, key), key)
                        .Match<object>(left: error => error, right: value => value)
                );
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(255)]
    public void Encode_rejects_attempts_outside_two_bits(byte attempt)
    {
        using var key = GroupChannelKey.StandardPublic();
        Assert.Equal(
            new GroupTextEncodeError(GroupTextEncodeErrorKind.InvalidAttempt),
            GroupTextCodec
                .Encode(new(0, "", attempt), key)
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Fact]
    public void Encode_distinguishes_embedded_NUL_and_invalid_Unicode()
    {
        using var key = GroupChannelKey.StandardPublic();
        Assert.Equal(
            new GroupTextEncodeError(GroupTextEncodeErrorKind.EmbeddedNul),
            GroupTextCodec
                .Encode(new(0, "a\0b"), key)
                .Match<object>(left: error => error, right: value => value)
        );
        Assert.Equal(
            new GroupTextEncodeError(GroupTextEncodeErrorKind.InvalidText),
            GroupTextCodec
                .Encode(new(0, new string('\ud800', 1)), key)
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Theory]
    [InlineData('x', 172, 172)]
    [InlineData('Ž', 86, 172)]
    public void Encode_returns_rejected_UTF8_byte_length(char character, int count, int length)
    {
        using var key = GroupChannelKey.StandardPublic();
        Assert.Equal(
            new GroupTextEncodeError(GroupTextEncodeErrorKind.InvalidTextLength, length),
            GroupTextCodec
                .Encode(new(0, new string(character, count)), key)
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Fact]
    public void Separates_malformed_channel_mismatch_MAC_failure_and_unsupported_text()
    {
        byte[] secret = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];
        using var key = new GroupChannelKey(secret);
        var payload = EncodeSuccess(new(0, "hello"), key);
        var otherChannel = payload.ToArray();
        otherChannel[0] ^= 1;
        Assert.Equal(
            new GroupTextDecodeError(GroupTextDecodeErrorKind.ChannelMismatch),
            GroupTextCodec
                .Decode(otherChannel, key)
                .Match<object>(left: error => error, right: value => value)
        );
        var changedMac = payload.ToArray();
        changedMac[1] ^= 1;
        Assert.Equal(
            new GroupTextDecodeError(GroupTextDecodeErrorKind.MacMismatch),
            GroupTextCodec
                .Decode(changedMac, key)
                .Match<object>(left: error => error, right: value => value)
        );
        var changedCiphertext = payload.ToArray();
        changedCiphertext[^1] ^= 1;
        Assert.Equal(
            new GroupTextDecodeError(GroupTextDecodeErrorKind.MacMismatch),
            GroupTextCodec
                .Decode(changedCiphertext, key)
                .Match<object>(left: error => error, right: value => value)
        );
        foreach (var length in new[] { 0, 3, 4, 18, 20, 195 })
        {
            Assert.Equal(
                new GroupTextDecodeError(GroupTextDecodeErrorKind.Malformed),
                GroupTextCodec
                    .Decode(new byte[length], key)
                    .Match<object>(left: error => error, right: value => value)
            );
        }

        Assert.Equal(
            new GroupTextDecodeError(GroupTextDecodeErrorKind.UnsupportedTextType),
            GroupTextCodec
                .Decode(InvalidTextPayload(secret, 4, 0xff), key)
                .Match<object>(left: error => error, right: value => value)
        );
        Assert.Equal(
            new GroupTextDecodeError(GroupTextDecodeErrorKind.Malformed),
            GroupTextCodec
                .Decode(InvalidTextPayload(secret, 0, 0xff), key)
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Fact]
    public void Key_ownership_is_defensive_disposable_and_nonserializing()
    {
        byte[] secret = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];
        using var key = new GroupChannelKey(secret);
        var expected = EncodeSuccess(new(0, "hello"), key);
        Array.Fill(secret, (byte)0xff);
        Assert.Equal(expected, EncodeSuccess(new(0, "hello"), key));
        // The span-only secret constructor is intentionally not a JSON contract.
        // System.Text.Json rejects it rather than serializing any secret bytes.
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(key));
        key.Dispose();
        Assert.Throws<ObjectDisposedException>(() => GroupTextCodec.Encode(new(0, "hello"), key));
        Assert.Throws<ObjectDisposedException>(() => GroupTextCodec.Encode(new(0, "a\0b", 4), key));
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
            Convert.ToHexStringLower(EncodeSuccess(new(0, ""), standard))
        );
        using var hashtag = GroupChannelKey.FromHashtag("#Test ");
        Assert.Equal(
            "0434817f4304cc4fdd0258a3bef93bfff669df",
            Convert.ToHexStringLower(EncodeSuccess(new(0, ""), hashtag))
        );
        Assert.Throws<ArgumentException>(() => GroupChannelKey.FromHashtag("test"));
    }

    private static byte[] EncodeSuccess(GroupTextMessage message, GroupChannelKey key) =>
        Assert.IsType<byte[]>(
            GroupTextCodec
                .Encode(message, key)
                .Match<object>(left: error => error, right: value => value)
        );

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
