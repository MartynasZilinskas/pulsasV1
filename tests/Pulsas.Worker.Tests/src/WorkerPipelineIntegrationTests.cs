using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Npgsql;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.GroupText;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.Persistence;
using Pulsas.Persistence.Chat;
using Pulsas.Persistence.Regions;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService.Delivery;
using Xunit;

namespace Pulsas.Worker.Tests;

public class WorkerPipelineIntegrationTests
{
    [PipelineTheory]
    [InlineData("žemaitija-e2e")]
    [InlineData(" ")]
    public async Task Worker_executable_projects_MQTT_reports_and_archives_duplicates_without_reprocessing(
        string region
    )
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var endpoint = Environment.GetEnvironmentVariable("PULSAS_WORKER_MQTT_ENDPOINT")!;
        Assert.True(new Uri(endpoint).IsLoopback);
        var admin = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = int.Parse(Environment.GetEnvironmentVariable("PULSAS_PGMQ_TEST_PORT")!),
            Username = "pgmq_test",
            Database = "postgres",
            Pooling = false,
        };
        var name = "worker_pipeline_" + Guid.NewGuid().ToString("N");
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync(token);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", control))
        {
            await create.ExecuteNonQueryAsync(token);
        }
        var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Database = name,
        };
        var root = FindRepositoryRoot();
        var scratch = Path.Combine(root, ".tmp", name);
        Directory.CreateDirectory(scratch);
        using var worker = new Process();
        var started = false;
        var output = new ConcurrentQueue<string>();
        try
        {
            await using var database = new TrackingDbContext(
                TrackingDatabase
                    .Configure(
                        new DbContextOptionsBuilder<TrackingDbContext>(),
                        connection.ConnectionString
                    )
                    .Options
            );
            await database.Database.MigrateAsync(token);
            database.Regions.Add(
                new StoredRegion
                {
                    Id = Guid.NewGuid(),
                    Code = region,
                    CountryCode = "LT",
                }
            );
            var retiredChannelId = Guid.NewGuid();
            database.Channels.Add(
                new Channel
                {
                    Id = retiredChannelId,
                    Kind = ChannelKind.Hashtag,
                    Name = "#retired",
                    Active = true,
                    Public = true,
                }
            );
            await database.SaveChangesAsync(token);
            worker.StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = scratch,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            worker.StartInfo.ArgumentList.Add(typeof(ReportQueueProcessor).Assembly.Location);
            // Never inherit operator secrets, telemetry exporters, Aspire endpoints or user-secrets settings.
            worker.StartInfo.Environment.Clear();
            worker.StartInfo.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH");
            worker.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            worker.StartInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
            worker.StartInfo.Environment["ConnectionStrings__pulsas"] = connection.ConnectionString;
            worker.StartInfo.Environment["DummyMqtt__Endpoint"] = endpoint;
            worker.StartInfo.Environment["DummyMqtt__Region"] = region;
            worker.StartInfo.Environment["chat__hashtags__0"] = "#qualification";
            worker.StartInfo.Environment["ReportDelivery__PollDelayMilliseconds"] = "25";
            worker.StartInfo.Environment["ReportDelivery__ContentConcurrency"] = "4";
            worker.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    output.Enqueue(e.Data);
                }
            };
            worker.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    output.Enqueue(e.Data);
                }
            };
            Assert.True(worker.Start());
            started = true;
            worker.BeginOutputReadLine();
            worker.BeginErrorReadLine();
            while (
                !output.Any(line =>
                    line.Contains("Dummy MQTT consumer subscribed", StringComparison.Ordinal)
                )
            )
            {
                Assert.False(worker.HasExited, string.Join(Environment.NewLine, output));
                await Task.Delay(25, token);
            }

            var retiredChannel = await database
                .Channels.AsNoTracking()
                .SingleAsync(channel => channel.Id == retiredChannelId, token);
            Assert.False(retiredChannel.Active);
            Assert.False(retiredChannel.Public);

            using var publisher = new MqttClientFactory().CreateMqttClient();
            await publisher.ConnectAsync(
                new MqttClientOptionsBuilder()
                    .WithProtocolVersion(MqttProtocolVersion.V311)
                    .WithWebSocketServer(server => server.WithUri(endpoint))
                    .Build(),
                token
            );
            using var sender = AdvertSigningKey.Generate();
            using var observerKey = AdvertSigningKey.Generate();
            var observer = Convert.ToHexStringLower(observerKey.PublicKey.AsSpan());
            var at = DateTimeOffset.UtcNow;
            var raw = ObserverReports.CreateAdvert(sender, 1, at);
            var advert = PacketCodec
                .Decode(Convert.FromHexString(raw))
                .Match(
                    left: error => throw new InvalidOperationException(error.ToString()),
                    right: packet => packet
                );
            var advertGroup =
                $"4:0:{Convert.ToHexStringLower(SHA256.HashData(advert.Payload.AsSpan()))}";
            async Task Publish(
                string packet,
                DateTimeOffset timestamp,
                object? origin,
                bool includeOrigin
            )
            {
                var report = new Dictionary<string, object?>
                {
                    ["type"] = "PACKET",
                    ["direction"] = "rx",
                    ["timestamp"] = timestamp,
                    ["raw"] = packet,
                    ["RSSI"] = -75,
                    ["SNR"] = 8.5,
                };
                if (includeOrigin)
                {
                    report["origin_id"] = origin;
                }
                await publisher.PublishAsync(
                    new MqttApplicationMessageBuilder()
                        .WithTopic($"meshcore/{region}/{observer}/packets")
                        .WithPayload(JsonSerializer.SerializeToUtf8Bytes(report))
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build(),
                    token
                );
            }
            await Publish(raw, at, null, false);
            await Publish(raw, at, 42, true);
            await Publish(raw, at, new string('b', 64), true);
            await Publish(raw, at.AddTicks(1), null, false);
            using var channelKey = GroupChannelKey.FromHashtag("#qualification");
            const string text = "[unverified] isolated MQTT qualification";
            foreach (byte attempt in new byte[] { 0, 1 })
            {
                var payload = GroupTextCodec
                    .Encode(new(checked((uint)at.ToUnixTimeSeconds()), text, attempt), channelKey)
                    .Match(
                        left: error => throw new InvalidOperationException(error.ToString()),
                        right: bytes => bytes
                    );
                var packet = Convert.ToHexStringLower(
                    PacketCodec.Encode(
                        PacketCodec.Create(RouteType.Flood, PacketType.GroupText, payload)
                    )
                );
                await Publish(packet, at, null, false);
            }
            await publisher.DisconnectAsync(new MqttClientDisconnectOptions(), token);
            await database.Database.OpenConnectionAsync(token);
            await using var counts = database.Database.GetDbConnection().CreateCommand();
            counts.CommandText =
                "SELECT (SELECT count(*) FROM pgmq.a_ingestion), (SELECT count(*) FROM pgmq.a_packet_content), (SELECT count(*) FROM pgmq.q_ingestion), (SELECT count(*) FROM pgmq.q_packet_content), (SELECT count(*) FROM pgmq.q_ingestion_quarantine)";
            while (true)
            {
                Assert.False(worker.HasExited, string.Join(Environment.NewLine, output));
                await using var reader = await counts.ExecuteReaderAsync(token);
                Assert.True(await reader.ReadAsync(token));
                if (reader.GetInt64(0) == 6 && reader.GetInt64(1) == 4)
                {
                    Assert.Equal(0L, reader.GetInt64(2));
                    Assert.Equal(0L, reader.GetInt64(3));
                    Assert.Equal(0L, reader.GetInt64(4));
                    break;
                }
                Assert.Equal(0L, reader.GetInt64(4));
                await Task.Delay(25, token);
            }
            await using var grouped = database.Database.GetDbConnection().CreateCommand();
            grouped.CommandText =
                "SELECT count(*) FROM pgmq.a_packet_content WHERE headers->>'x-pgmq-group' = @group";
            var groupParameter = grouped.CreateParameter();
            groupParameter.ParameterName = "group";
            groupParameter.Value = advertGroup;
            grouped.Parameters.Add(groupParameter);
            Assert.Equal(2L, await grouped.ExecuteScalarAsync(token));

            var receptions = await database
                .MeshPacketReceptions.AsNoTracking()
                .Include(row => row.Observer)
                .Include(row => row.Packet)
                .ToListAsync(token);
            Assert.Equal(4, receptions.Count);
            Assert.All(receptions, row =>
            {
                Assert.Equal(observer, Convert.ToHexStringLower(row.Observer.PublicKey));
                Assert.Equal(region, row.RegionCode);
                Assert.NotNull(row.ReceptionIdentity);
                Assert.Null(row.HeardAt);
            });
            var node = await database.MeshNodes.SingleAsync(token);
            Assert.Equal(sender.PublicKey.ToArray(), node.PublicKey);
            Assert.Equal("synthetic-1", node.Name);
            var message = await database.ChatMessages.SingleAsync(token);
            Assert.Equal(text, message.Text);
            Assert.Null(message.ClaimedSenderName);
            Assert.Equal(2, receptions.Count(row => row.Packet!.ChatMessageId == message.Id));
            Assert.Equal(
                2,
                output.Count(line => line.Contains("Duplicate reception", StringComparison.Ordinal))
            );
            Assert.DoesNotContain(output, line =>
                line.Contains(text, StringComparison.Ordinal)
                || line.Contains(raw, StringComparison.Ordinal)
            );
        }
        finally
        {
            if (started && !worker.HasExited)
            {
                worker.Kill(entireProcessTree: true);
                await worker.WaitForExitAsync();
            }
            if (started)
            {
                await worker.WaitForExitAsync(CancellationToken.None);
            }
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Pulsas.slnx")))
        {
            directory = directory.Parent
                ?? throw new InvalidOperationException("Run from the repository checkout.");
        }
        return directory.FullName;
    }

    private class PipelineTheoryAttribute : TheoryAttribute
    {
        public PipelineTheoryAttribute()
        {
            if (
                Environment.GetEnvironmentVariable("PULSAS_PGMQ_TEST_PORT") is null
                || string.IsNullOrEmpty(
                    Environment.GetEnvironmentVariable("PULSAS_WORKER_MQTT_ENDPOINT")
                )
            )
            {
                Skip =
                    "Set both isolated PGMQ port and loopback MQTT endpoint to qualify the real Worker executable.";
            }
        }
    }
}
