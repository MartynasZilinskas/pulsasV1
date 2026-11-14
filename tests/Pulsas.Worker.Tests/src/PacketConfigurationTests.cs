using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pulsas.MeshCore.Payloads.GroupText;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.Worker.ApiService.Configuration;
using Xunit;

namespace Pulsas.Worker.Tests;

public class PacketConfigurationTests
{
    [Fact]
    public void Shipped_public_configuration_is_consumed_from_the_build_output()
    {
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "configuration", "chat.json"))
                .Build();
        var snapshot = PacketConfiguration.Read(configuration);
        Assert.Equal(20, snapshot.Channels.Length);
        Assert.Equal(ChannelKind.StandardPublic, snapshot.Channels[0].Kind);
        Assert.Equal("Public", snapshot.Channels[0].Name);
        Assert.All(snapshot.Channels, channel => Assert.True(channel.Public));
        Assert.All(snapshot.Channels, channel => Assert.Null(channel.ConfigurationId));
        using var publicKey = snapshot.Channels[0].CreateKey();
        Assert.Equal(0x11, publicKey.ChannelHash);
        Assert.Equal("#hamradio", snapshot.Channels[^1].Name);
        Assert.Empty(snapshot.Scopes);
    }

    [Fact]
    public void Explicit_channels_keep_operator_identity_and_can_decrypt_without_persisted_key_hashes()
    {
        using var configuration = Build(
            """
            {"chat":{"explicit":{
              "one":{"name":"Team","public":false,"key":"AAAAAAAAAAAAAAAAAAAAAA=="},
              "two":{"name":"Team","public":false,"key":"AAAAAAAAAAAAAAAAAAAAAA=="}
            }}}
            """
        );
        var snapshot = PacketConfiguration.Read(configuration);
        Assert.Equal(new[] { "one", "two" }, snapshot.Channels.Select(c => c.ConfigurationId));
        using var key = snapshot.Channels[0].CreateKey();
        var payload = Assert.IsType<byte[]>(
            GroupTextCodec
                .Encode(new(1, "test", 0), key)
                .Match<object>(left: error => error, right: value => value)
        );
        using var second = snapshot.Channels[1].CreateKey();
        Assert.Equal(
            new GroupTextMessage(1, "test"),
            GroupTextCodec
                .Decode(payload, second)
                .Match<object>(left: error => error, right: value => value)
        );
        var serialized = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAA==", serialized);
        Assert.DoesNotContain("\"Key\"", serialized);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAA==", snapshot.Channels[0].ToString());
    }

    [Fact]
    public void Scope_snapshot_supports_name_derived_and_secret_keys_without_disclosing_them()
    {
        using var configuration = Build(
            """
            {"scopes":[{"name":"vilnius"},{"name":"#eu"},
              {"name":"$team","key":"AAAAAAAAAAAAAAAAAAAAAA=="}]}
            """
        );
        var snapshot = PacketConfiguration.Read(configuration);
        Assert.Equal(new[] { "#vilnius", "#eu", "$team" }, snapshot.Scopes.Select(s => s.Name));
        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("\"Key\"", json);
        Assert.DoesNotContain("KeySource", json);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAA==", json);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAA==", snapshot.Scopes[2].ToString());
    }

    [Theory]
    [InlineData("""{"chat":{"explicit":{"team":{"name":"Team","key":"SECRET_SENTINEL"}}}}""")]
    [InlineData("""{"chat":{"hashtags":["test","#test"]}}""")]
    [InlineData("""{"chat":{"hashtags":["##test"]}}""")]
    [InlineData("""{"chat":{"explicit":{"team":{"key":"SECRET_SENTINEL"}}}}""")]
    [InlineData("""{"scopes":[{"name":"$team"}]}""")]
    [InlineData("""{"scopes":[{"name":"$team","key":"SECRET_SENTINEL"}]}""")]
    [InlineData("""{"scopes":[{"name":"$team","key":"AA=="}]}""")]
    [InlineData("""{"scopes":[{"name":"*"}]}""")]
    [InlineData("""{"scopes":[{"name":"test"},{"name":"#test"}]}""")]
    [InlineData("""{"chat":{"standard_public":"invalid"}}""")]
    public void Invalid_effective_configuration_fails_without_secret_values(string json)
    {
        using var configuration = Build(json);
        var error = Assert.Throws<InvalidOperationException>(() =>
            PacketConfiguration.Read(configuration)
        );
        Assert.DoesNotContain("SECRET_SENTINEL", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void Explicit_channel_supports_both_codec_key_lengths(int length)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["chat:explicit:team:name"] = "Team",
                        ["chat:explicit:team:public"] = "false",
                        ["chat:explicit:team:key"] = secret,
                    }
                )
                .Build();
        using var key = Assert.Single(ChatConfiguration.Read(configuration)).CreateKey();
        var payload = Assert.IsType<byte[]>(
            GroupTextCodec
                .Encode(new(1, "test", 0), key)
                .Match<object>(left: error => error, right: value => value)
        );
        Assert.Equal(
            new GroupTextMessage(1, "test"),
            GroupTextCodec
                .Decode(payload, key)
                .Match<object>(left: error => error, right: value => value)
        );
    }

    private static ConfigurationRoot Build(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
    }
}
