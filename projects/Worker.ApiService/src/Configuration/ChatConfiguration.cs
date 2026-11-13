using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Pulsas.PacketProcessing.Contracts.Chat;

namespace Pulsas.Worker.ApiService.Configuration;

public static class ChatConfiguration
{
    public static ImmutableArray<ConfiguredChannel> Read(IConfiguration configuration)
    {
        var chat = configuration.GetSection("chat");
        ConfigurationValues.Members(chat, "standard_public", "hashtags", "explicit");
        var result = ImmutableArray.CreateBuilder<ConfiguredChannel>();
        if (ConfigurationValues.Boolean(chat, "standard_public", false))
        {
            Add(
                ChannelKind.StandardPublic,
                "Public",
                null,
                true,
                Convert.FromBase64String("izOH6cXN6mrJ5e26oRXNcg==")
            );
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in ConfigurationValues.List(chat.GetSection("hashtags")))
        {
            var name = ConfigurationValues.PublicName(ConfigurationValues.Text(section));
            if (!names.Add(name))
            {
                throw new InvalidOperationException("Duplicate normalized hashtag channel name.");
            }
            Add(
                ChannelKind.Hashtag,
                name,
                null,
                true,
                SHA256.HashData(Encoding.UTF8.GetBytes(name))[..16]
            );
        }
        var explicitChannels = chat.GetSection("explicit");
        if (explicitChannels.Value is not null and not "")
        {
            throw new InvalidOperationException(
                "Explicit chat channels must be a map keyed by configuration ID."
            );
        }
        foreach (var section in explicitChannels.GetChildren())
        {
            ConfigurationValues.Members(section, "name", "key", "public");
            if (string.IsNullOrWhiteSpace(section.Key) || section.Key != section.Key.Trim())
            {
                throw new InvalidOperationException(
                    "Explicit chat channels require a nonempty configuration ID."
                );
            }
            var name = ConfigurationValues.Text(section.GetSection("name"));
            var publication = ConfigurationValues.Boolean(section, "public", true);
            Add(
                ChannelKind.ExplicitKey,
                name,
                section.Key,
                publication,
                ConfigurationKeys.Read(section, allow32Bytes: true)
            );
        }
        return result.ToImmutable();

        void Add(ChannelKind kind, string name, string? id, bool publication, byte[] key)
        {
            try
            {
                result.Add(new(kind, name, id, publication, key));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }
}
