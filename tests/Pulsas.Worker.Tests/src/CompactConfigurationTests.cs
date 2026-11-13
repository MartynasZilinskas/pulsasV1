using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.Worker.ApiService.Configuration;
using Xunit;

namespace Pulsas.Worker.Tests;

public class CompactConfigurationTests
{
    private const string Geography = """
        "regions": {
          "east-lithuania": {"name":"Rytų Lietuva","country":"LT","center":[55.036,25.61],"border_file":"borders/east.geojson","zoom":8},
          "VNO": {"name":"Vilnius","country":"LT","center":[54.7,25.3]},
          "standalone": {"name":"Standalone","country":"LV","center":[90,-180]}
        },
        "region_groups": {
          "lithuania": {"name":"Lietuva","regions":["east-lithuania","VNO"],"center":[55.2,23.75],"zoom":6},
          "overlap": {"name":"Overlap","regions":["east-lithuania"]}
        }
        """;

    [Fact]
    public void Missing_chat_disables_builtin_and_listed_channels_default_to_public()
    {
        using var empty = Build("{}");
        Assert.Empty(PacketConfiguration.Read(empty).Channels);
        using var configuration = Build(
            """
            {"chat":{"standard_public":false,"hashtags":["Test","#test"],"explicit":{
              "team":{"name":"Team","key":"AAAAAAAAAAAAAAAAAAAAAA=="},
              "private":{"name":"Private","key":"AAAAAAAAAAAAAAAAAAAAAA==","public":false}
            }}}
            """
        );
        var channels = PacketConfiguration.Read(configuration).Channels;
        Assert.Equal(new[] { "#Test", "#test" }, channels.Take(2).Select(c => c.Name));
        Assert.DoesNotContain(channels, c => c.Kind == ChannelKind.StandardPublic);
        Assert.All(channels.Where(c => c.ConfigurationId != "private"), c => Assert.True(c.Public));
        Assert.False(channels.Single(c => c.ConfigurationId == "private").Public);
    }

    [Fact]
    public void Geography_supports_generic_ids_standalone_and_overlapping_groups()
    {
        using var configuration = Build(
            "{" + Geography + ",\"default_view\":{\"region_group\":\"lithuania\"}}"
        );
        var snapshot = PacketConfiguration.Read(configuration);
        Assert.Equal(3, snapshot.Regions.Count);
        Assert.Equal(
            new ConfiguredCenter(55.036, 25.61),
            snapshot.Regions["east-lithuania"].Center
        );
        Assert.Equal("borders/east.geojson", snapshot.Regions["east-lithuania"].BorderFile);
        Assert.Equal(8, snapshot.Regions["east-lithuania"].Zoom);
        Assert.All(snapshot.RegionGroups.Values, group =>
            Assert.Contains("east-lithuania", group.Regions)
        );
        Assert.DoesNotContain(snapshot.RegionGroups.Values, group =>
            group.Regions.Contains("standalone")
        );
        Assert.Null(snapshot.RegionGroups["overlap"].Center);
        Assert.Equal(new ConfiguredDefaultView(null, "lithuania"), snapshot.DefaultView);
        configuration["regions:east-lithuania:name"] = "Changed";
        Assert.Equal("Rytų Lietuva", snapshot.Regions["east-lithuania"].Name);
    }

    [Fact]
    public void Default_may_target_region_or_be_absent()
    {
        using var direct = Build("{" + Geography + ",\"default_view\":{\"region\":\"VNO\"}}");
        Assert.Equal(
            new ConfiguredDefaultView("VNO", null),
            PacketConfiguration.Read(direct).DefaultView
        );
        using var overview = Build("{" + Geography + "}");
        Assert.Null(PacketConfiguration.Read(overview).DefaultView);
    }

    [Theory]
    [InlineData("""{"region_groups":{"group":{"name":"Group","regions":["VNO","VNO"]}}}""")]
    [InlineData("""{"region_groups":{"group":{"name":"Group","regions":["vno"]}}}""")]
    [InlineData("""{"region_groups":{"group":{"name":"Group","regions":["VNO"],"zoom":-1}}}""")]
    [InlineData(
        """{"region_groups":{"group":{"name":"Group","regions":["VNO"],"zoom":"Infinity"}}}"""
    )]
    public void Group_membership_and_camera_settings_are_validated(string json)
    {
        using var configuration = Build("{" + Geography + "}", json);
        Assert.Throws<InvalidOperationException>(() => PacketConfiguration.Read(configuration));
    }

    [Theory]
    [InlineData("""{"default_view":{}}""")]
    [InlineData("""{"default_view":null}""")]
    [InlineData("""{"default_view":{"region":"absent"}}""")]
    [InlineData("""{"default_view":{"region":"VNO","region_group":"lithuania"}}""")]
    [InlineData("""{"regions":{"east":{"name":"East","country":"LT","center":[91,0]}}}""")]
    [InlineData("""{"regions":{"east":{"name":"East","country":"LT","center":[0,181]}}}""")]
    [InlineData("""{"regions":{"east":{"name":"East","country":"LT","center":["NaN",0]}}}""")]
    [InlineData("""{"regions":{"east":{"name":"East","country":"LT","center":[0]}}}""")]
    [InlineData("""{"regions":{"east":{"name":"East","country":"lt","center":[0,0]}}}""")]
    [InlineData("""{"regions":{"east":{"name":"East","country":"LT"}}}""")]
    [InlineData("""{"region_groups":{"group":{"name":"Group","regions":["absent"]}}}""")]
    [InlineData("""{"region_groups":{"group":{"name":"Group","regions":[]}}}""")]
    [InlineData("""{"chat":{"standard_public":{}}}""")]
    [InlineData("""{"chat":{"standard_public":null}}""")]
    [InlineData("""{"chat":{"active":true}}""")]
    [InlineData("""{"chat":{"hashtags":[""]}}""")]
    [InlineData("""{"chat":{"hashtags":["#"]}}""")]
    [InlineData("""{"chat":{"hashtags":[" test"]}}""")]
    [InlineData("""{"chat":{"hashtags":[{"name":"test"}]}}""")]
    [InlineData(
        """{"chat":{"explicit":{"team":{"name":"Team","key":"AAAAAAAAAAAAAAAAAAAAAA==","public":"oops"}}}}"""
    )]
    [InlineData("""{"scopes":[{"name":"##eu"}]}""")]
    [InlineData("""{"scopes":[{"name":"$$team","key":"AAAAAAAAAAAAAAAAAAAAAA=="}]}""")]
    [InlineData("""{"scopes":[{"name":"eu","key":"AAAAAAAAAAAAAAAAAAAAAA=="}]}""")]
    [InlineData("""{"chat":{"channels":[]}}""")]
    [InlineData("""{"chat":{"channels":null}}""")]
    [InlineData("""{"transport":{"scopes":[]}}""")]
    [InlineData("""{"transport":{"scopes":null}}""")]
    public void Invalid_owned_configuration_is_rejected(string json)
    {
        using var configuration = Build(json);
        Assert.Throws<InvalidOperationException>(() => PacketConfiguration.Read(configuration));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    public void Empty_declarations_clear_lower_catalogues(string empty)
    {
        using var configuration = Build(
            """
            {"chat":{"hashtags":["one","two"],"explicit":{"team":{"name":"Team","key":"AAAAAAAAAAAAAAAAAAAAAA=="}}},"scopes":[{"name":"eu"}]}
            """,
            "{\"chat\":{\"hashtags\":"
                + empty
                + ",\"explicit\":"
                + empty
                + "},\"scopes\":"
                + empty
                + "}"
        );
        var snapshot = PacketConfiguration.Read(configuration);
        Assert.Empty(snapshot.Channels);
        Assert.Empty(snapshot.Scopes);
    }

    [Fact]
    public void Declarative_catalogues_replace_while_omitted_catalogues_inherit()
    {
        using var configuration = Build(
            "{"
                + Geography
                + ",\"chat\":{\"hashtags\":[\"one\",\"two\"]},\"scopes\":[{\"name\":\"eu\"},{\"name\":\"lt\"}]}",
            """{"CHAT":{"HASHTAGS":["replacement"]},"scopes":[{"name":"baltics"}],"regions_extra":{}}"""
        );
        var snapshot = PacketConfiguration.Read(configuration);
        Assert.Equal("#replacement", Assert.Single(snapshot.Channels).Name);
        Assert.Equal("#baltics", Assert.Single(snapshot.Scopes).Name);
        Assert.Equal(3, snapshot.Regions.Count);
    }

    [Fact]
    public void Replaced_explicit_catalogue_cannot_borrow_lower_secret()
    {
        using var configuration = Build(
            """{"chat":{"explicit":{"team":{"name":"Team","key":"AAAAAAAAAAAAAAAAAAAAAA=="}}}}""",
            """{"chat":{"explicit":{"team":{"name":"Replacement"}}}}"""
        );
        Assert.Throws<InvalidOperationException>(() => PacketConfiguration.Read(configuration));
    }

    [Fact]
    public void Actual_host_stack_keeps_environment_and_command_line_leaf_overrides()
    {
        var prefix = "PULSAS_CONFIG_TEST_" + Guid.NewGuid().ToString("N") + "_";
        Environment.SetEnvironmentVariable(prefix + "chat__hashtags__0", "environment");
        Environment.SetEnvironmentVariable(prefix + "chat__explicit__team__public", "false");
        try
        {
            var builder = WebApplication.CreateBuilder([]);
            using var configuration = builder.Configuration;
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(
                    """
                    {"chat":{"hashtags":["first","second"],"explicit":{"team":{"name":"Team","key":"AAAAAAAAAAAAAAAAAAAAAA=="}}}}
                    """
                )
            );
            configuration.AddJsonStream(stream);
            configuration.AddEnvironmentVariables(prefix);
            configuration.AddCommandLine(["--chat:hashtags:0", "command-line"]);
            var snapshot = PacketConfiguration.Read(configuration);
            Assert.Equal(
                new[] { "#command-line", "#second" },
                snapshot.Channels.Take(2).Select(c => c.Name)
            );
            Assert.False(snapshot.Channels.Single(c => c.ConfigurationId == "team").Public);
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "chat__hashtags__0", null);
            Environment.SetEnvironmentVariable(prefix + "chat__explicit__team__public", null);
        }
    }

    private static ConfigurationRoot Build(params string[] layers)
    {
        var builder = new ConfigurationBuilder();
        foreach (var json in layers)
        {
            using var layer = (ConfigurationRoot)
                new ConfigurationBuilder()
                    .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    .Build();
            // Preserve the JSON provider's explicit null/empty markers, not implicit parent keys.
            var provider = layer.Providers.Single();
            builder.AddInMemoryCollection(
                layer.AsEnumerable().Where(entry => provider.TryGet(entry.Key, out _))
            );
        }
        return (ConfigurationRoot)builder.Build();
    }
}
