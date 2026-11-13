using Microsoft.Extensions.Configuration;
using Pulsas.Configuration;
using Pulsas.Worker.ApiService.Configuration;
using Xunit;

namespace Pulsas.Worker.Tests;

public class SecretJsonConfigurationTests : IDisposable
{
    private readonly string directory;

    public SecretJsonConfigurationTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pulsas.slnx")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        directory = Path.Combine(
            root.FullName,
            ".tmp",
            "configuration-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    [Fact]
    public void File_and_directory_layers_load_mounted_settings()
    {
        var path = Write(
            "chat.json",
            """{"Chat":{"Channels":[{"Kind":"StandardPublic","Public":true}]}}"""
        );
        Write("transport.json", """{"Transport":{"Scopes":[{"Name":"#test"}]}}""");
        using var single = (ConfigurationRoot)
            new ConfigurationBuilder().AddSecretJsonFiles(path).Build();
        using var multiple = (ConfigurationRoot)
            new ConfigurationBuilder().AddSecretJsonFiles(directory).Build();
        Assert.Equal("StandardPublic", single["Chat:Channels:0:Kind"]);
        Assert.Null(single["Transport:Scopes:0:Name"]);
        Assert.Equal("#test", multiple["Transport:Scopes:0:Name"]);
    }

    [Fact]
    public void Explicit_missing_or_blank_paths_fail_but_null_is_a_noop()
    {
        var builder = new ConfigurationBuilder();
        Assert.Same(builder, builder.AddSecretJsonFiles(null));
        Assert.Empty(builder.Sources);
        Assert.Throws<ArgumentException>(() => builder.AddSecretJsonFiles(" "));
        Assert.Throws<FileNotFoundException>(() =>
            builder.AddSecretJsonFiles(Path.Combine(directory, "missing.json"))
        );
        Assert.Throws<FormatException>(() => builder.AddSecretJsonFiles(directory));
        Assert.Empty(builder.Sources);
    }

    [Theory]
    [InlineData("{\"Secret:First\":1}", "{\"SECRET\":{\"Other\":2}}")]
    [InlineData("{\"Secret\":null}", "{\"secret\":{}}")]
    public void Case_insensitive_section_ownership_rejects_overlaps_atomically(
        string first,
        string second
    )
    {
        Write("a.json", first);
        Write("b.json", second);
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Original"] = "retained" }
        );
        var sources = configuration.Sources.ToArray();
        Assert.Throws<FormatException>(() => configuration.AddSecretJsonFiles(directory));
        Assert.Equal(sources, configuration.Sources);
        Assert.Equal("retained", configuration["Original"]);
    }

    [Theory]
    [InlineData("{\"Password\":SECRET_SENTINEL}")]
    [InlineData("{\"SECRET_SENTINEL\":{},\"secret_sentinel\":{}}")]
    [InlineData("{\"Nested\":{\"SECRET_SENTINEL\":1,\"secret_sentinel\":2}}")]
    [InlineData("[]")]
    public void Content_errors_are_redacted_and_leave_configuration_unchanged(string json)
    {
        Write("a.json", """{"Good":"value"}""");
        Write("z.json", json);
        var builder = new ConfigurationBuilder();
        var error = Assert.Throws<FormatException>(() => builder.AddSecretJsonFiles(directory));
        Assert.DoesNotContain(
            "SECRET_SENTINEL",
            error.ToString(),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Null(error.InnerException);
        Assert.Empty(builder.Sources);
    }

    [Fact]
    public void Snapshot_matches_json_provider_and_does_not_reload_files()
    {
        const string json = """
            { // Comments and trailing commas follow the normal JSON provider.
              "Parent":{"Child":"value"},"Empty":[],"Null":null,
            }
            """;
        var path = Write("settings.json", json);
        var defaults = new Dictionary<string, string?>
        {
            ["Parent"] = "parent",
            ["Null"] = "before",
        };
        using var expected = (ConfigurationRoot)
            new ConfigurationBuilder().AddInMemoryCollection(defaults).AddJsonFile(path).Build();
        using var actual = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddInMemoryCollection(defaults)
                .AddSecretJsonFiles(path)
                .Build();
        Assert.Equal(
            expected.AsEnumerable().OrderBy(x => x.Key),
            actual.AsEnumerable().OrderBy(x => x.Key)
        );
        File.WriteAllText(path, """{"Parent":{"Child":"changed"}}""");
        actual.Reload();
        Assert.Equal("value", actual["Parent:Child"]);
    }

    [Fact]
    public void Environment_then_command_line_override_secret_snapshot()
    {
        var path = Write("settings.json", """{"Value":"file"}""");
        var prefix = "PULSAS_TEST_" + Guid.NewGuid().ToString("N") + "_";
        Environment.SetEnvironmentVariable(prefix + "Value", "environment");
        try
        {
            using var configuration = (ConfigurationRoot)
                new ConfigurationBuilder()
                    .AddSecretJsonFiles(path)
                    .AddEnvironmentVariables(prefix)
                    .AddCommandLine(["--Value", "command-line"])
                    .Build();
            Assert.Equal("command-line", configuration["Value"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "Value", null);
        }
    }

    [Fact]
    public void Shorter_arrays_do_not_remove_lower_layer_entries()
    {
        var path = Write("settings.json", """{"Chat":{"Channels":[{"Name":"replacement"}]}}""");
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Chat:Channels:0:Name"] = "old-first",
                        ["Chat:Channels:1:Name"] = "retained-second",
                    }
                )
                .AddSecretJsonFiles(path)
                .Build();
        Assert.Equal("replacement", configuration["Chat:Channels:0:Name"]);
        Assert.Equal("retained-second", configuration["Chat:Channels:1:Name"]);
    }

    [Fact]
    public void Worker_replaces_catalogues_from_mounted_files_before_applying_cli_patches()
    {
        var lower = Write(
            "lower.json",
            """
            {"chat":{"hashtags":["old","retained"],"explicit":{"removed":{"name":"Removed","key":"AAAAAAAAAAAAAAAAAAAAAA=="}}},
             "scopes":[{"name":"eu"},{"name":"lt"}],
             "regions":{"old":{"name":"Old","country":"LT","center":[0,0]}},
             "region_groups":{"old":{"name":"Old","regions":["old"]}},
             "default_view":{"region_group":"old"}}
            """
        );
        var replacement = Write(
            "replacement.json",
            """
            {"chat":{"hashtags":["replacement"],"explicit":{}},"scopes":[],
             "regions":{"east":{"name":"East","country":"LT","center":[55,25]}},
             "region_groups":{},"default_view":{"region":"east"}}
            """
        );
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddJsonFile(lower)
                .AddSecretJsonFiles(replacement)
                .AddCommandLine(["--chat:hashtags:0", "cli"])
                .Build();
        var snapshot = PacketConfiguration.Read(configuration);
        Assert.Equal("#cli", Assert.Single(snapshot.Channels).Name);
        Assert.Empty(snapshot.Scopes);
        Assert.Equal("east", Assert.Single(snapshot.Regions).Key);
        Assert.Empty(snapshot.RegionGroups);
        Assert.Equal(new ConfiguredDefaultView("east", null), snapshot.DefaultView);
    }

    [Fact]
    public void Worker_validates_only_final_declarations_not_replaced_incomplete_entries()
    {
        var replacement = Write(
            "replacement.json",
            """{"chat":{"explicit":{"team":{"name":"Team","key":"AAAAAAAAAAAAAAAAAAAAAA=="}}}}"""
        );
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["chat:explicit:invalid:public"] = "not-a-boolean",
                    }
                )
                .AddSecretJsonFiles(replacement)
                .Build();
        Assert.Equal(
            "team",
            Assert.Single(PacketConfiguration.Read(configuration).Channels).ConfigurationId
        );
    }

    [Fact]
    public void Projected_symlink_files_are_loaded_without_recursion()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var projection = Directory.CreateDirectory(Path.Combine(directory, "..projection"));
        File.WriteAllText(
            Path.Combine(projection.FullName, "settings.json"),
            """{"Value":"projected"}"""
        );
        File.CreateSymbolicLink(
            Path.Combine(directory, "settings.json"),
            Path.Combine("..projection", "settings.json")
        );
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder().AddSecretJsonFiles(directory).Build();
        Assert.Equal("projected", configuration["Value"]);
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, json);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return path;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
