#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Database;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Serilog.Events;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// End-to-end latency profile of "send a 1:1 chat text message": Frodo uploads a chat-shaped
/// encrypted file with Sam as recipient (leg 1, the client-facing POST), Frodo's outbox is drained
/// to Sam's peer endpoint (leg 2), Sam's inbox is processed into the drive (leg 3). Per leg it
/// records wall clock, DB statement counts and every <c>[…Timing]</c> log line the server emits.
/// Not a correctness test — run explicitly, compare variants by subclassing with different
/// <see cref="V2Fixture.ConfigOverrides"/>.
/// </summary>
[TestFixture, Explicit("perf benchmark, not a regression test")]
public class ChatSendBenchmarkTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    protected virtual string VariantName => Environment.GetEnvironmentVariable("ODIN_BENCH_VARIANT") ?? "baseline";

    private static readonly Guid ChatAppId = Guid.Parse("2d781401-3804-4b57-b4aa-d8e4e2ef39f4");
    private const int ChatMessageFileType = 7878;

    private static int Warmup => EnvInt("ODIN_BENCH_WARMUP", 20);
    private static int Iterations => EnvInt("ODIN_BENCH_ITERATIONS", 200);

    private static int EnvInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

    [Test]
    public async Task SendTextMessage_FrodoToSam()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "chat",
            allowAnonymousReads: false);

        var counters = Host.Server.Services.GetRequiredService<DatabaseCounters>();
        _logStore = Host.Server.Services.GetRequiredService<ILogEventMemoryStore>();

        var conversationId = Guid.NewGuid();
        var legs = new Dictionary<string, LegSamples>
        {
            ["1-upload"] = new(), ["2-outbox"] = new(), ["3-inbox"] = new(),
        };
        _timingLines = new TimingLineAggregator();
        var timingLines = _timingLines;
        var total = Warmup + Iterations;

        // Lets an operator attach dotnet-trace to the test host before the loop starts.
        var startDelayMs = EnvInt("ODIN_BENCH_START_DELAY_MS", 0);
        if (startDelayMs > 0)
        {
            TestContext.Out.WriteLine($"pid {Environment.ProcessId}: waiting {startDelayMs} ms before iterating");
            await Task.Delay(startDelayMs);
        }

        for (var i = 0; i < total; i++)
        {
            var measured = i >= Warmup;
            var uniqueId = Guid.NewGuid();
            var metadata = new UploadFileMetadata
            {
                AllowDistribution = true,
                IsEncrypted = true,
                AccessControlList = AccessControlList.Connected,
                AppData = new UploadAppFileMetaData
                {
                    FileType = ChatMessageFileType,
                    UniqueId = uniqueId,
                    GroupId = conversationId,
                    UserDate = UnixTimeUtc.Now(),
                    Tags = [],
                    Content = "{\"message\":\"" + new string('x', 200) + "\",\"replyId\":null}",
                },
            };
            var transit = new TransitOptions { Recipients = [sam.Identity] };
            var notification = new AppNotificationOptions
            {
                AppId = ChatAppId,
                TypeId = conversationId,
                TagId = uniqueId,
                Silent = false,
                UnEncryptedMessage = "New message",
            };

            var (upload, gtid) = await Timed("1-upload", legs["1-upload"], counters, measured, async () =>
            {
                var (resp, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(
                    drive.Alias, metadata, transit, notificationOptions: notification);
                Assert.That(resp.IsSuccessStatusCode, Is.True, $"upload failed: {resp.StatusCode}");
                return (resp.Content!, resp.Content!.GlobalTransitId);
            });
            Assert.That(gtid, Is.Not.Null);
            Assert.That(upload.RecipientStatus[sam.Identity], Is.EqualTo(TransferStatus.Enqueued),
                $"iteration {i}: recipient status {upload.RecipientStatus[sam.Identity]}");

            await Timed("2-outbox", legs["2-outbox"], counters, measured, async () =>
            {
                await frodo.Sync.DrainOutboxAsync();
                return 0;
            });
            Assert.That(await frodo.Sync.IsOutboxEmptyAsync(drive), Is.True, $"iteration {i}: outbox not drained");

            var inbox = await Timed("3-inbox", legs["3-inbox"], counters, measured,
                () => sam.Sync.ProcessInboxAsync(drive));
            Assert.That(inbox.TotalItems, Is.EqualTo(0), $"iteration {i}: {inbox.TotalItems} inbox items left");

            if (i % 25 == 0)
            {
                var query = await sam.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
                {
                    QueryParams = new FileQueryParamsV1 { GlobalTransitId = [gtid!.Value] },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 2, IncludeMetadataHeader = true },
                });
                Assert.That(query.IsSuccessStatusCode, Is.True);
                Assert.That(query.Content!.SearchResults.Count(), Is.EqualTo(1), $"iteration {i}: Sam does not have the file");
            }
        }

        var report = Render(legs, timingLines);
        TestContext.Out.WriteLine(report);
        await WriteArtifacts(legs, report);
    }

    private ILogEventMemoryStore _logStore = null!;
    private TimingLineAggregator _timingLines = null!;
    private readonly SqlCensus _sqlCensus = new();

    private async Task<T> Timed<T>(string legName, LegSamples leg, DatabaseCounters counters, bool record, Func<Task<T>> body)
    {
        _logStore.Clear();
        var before = Snapshot(counters);
        var sw = Stopwatch.StartNew();
        var result = await body();
        sw.Stop();
        if (record)
        {
            leg.Add(sw.Elapsed.TotalMilliseconds, Snapshot(counters), before);
            var events = _logStore.GetLogEvents();
            _timingLines.Harvest(events);
            _sqlCensus.Harvest(legName, events);
        }
        return result;
    }

    private static (long nonQuery, long reader, long scalar, long opened) Snapshot(DatabaseCounters c)
        => (c.NoDbExecuteNonQueryAsync, c.NoDbExecuteReaderAsync, c.NoDbExecuteScalarAsync, c.NoDbOpened);

    private string Render(Dictionary<string, LegSamples> legs, TimingLineAggregator lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Chat send benchmark — variant `{VariantName}`, {Iterations} measured iterations ({Warmup} warm-up)");
        sb.AppendLine();
        sb.AppendLine("| leg | p50 ms | p95 ms | min ms | mean ms | nonQuery | reader | scalar | conn opened |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (name, leg) in legs.OrderBy(kv => kv.Key))
        {
            sb.AppendLine(
                $"| {name} | {P(leg.Ms, 50):F2} | {P(leg.Ms, 95):F2} | {leg.Ms.Min():F2} | {leg.Ms.Average():F2} " +
                $"| {leg.NonQuery.Average():F1} | {leg.Reader.Average():F1} | {leg.Scalar.Average():F1} | {leg.Opened.Average():F1} |");
        }
        var e2e = legs.Values.Select(l => l.Ms).Aggregate((a, b) => a.Zip(b, (x, y) => x + y).ToList());
        sb.AppendLine($"| **end-to-end** | {P(e2e, 50):F2} | {P(e2e, 95):F2} | {e2e.Min():F2} | {e2e.Average():F2} | | | | |");
        sb.AppendLine();
        sb.AppendLine(lines.Render());
        return sb.ToString();
    }

    private async Task WriteArtifacts(Dictionary<string, LegSamples> legs, string report)
    {
        var dir = Environment.GetEnvironmentVariable("ODIN_BENCH_OUT") ?? TestContext.CurrentContext.WorkDirectory;
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(Path.Combine(dir, $"chat-send-{VariantName}-{stamp}.md"), report);

        var csv = new StringBuilder("iteration,leg,ms,nonQuery,reader,scalar,opened\n");
        foreach (var (name, leg) in legs)
        {
            for (var i = 0; i < leg.Ms.Count; i++)
            {
                csv.Append(CultureInfo.InvariantCulture,
                    $"{i},{name},{leg.Ms[i]:F3},{leg.NonQuery[i]},{leg.Reader[i]},{leg.Scalar[i]},{leg.Opened[i]}\n");
            }
        }
        await File.WriteAllTextAsync(Path.Combine(dir, $"chat-send-{VariantName}-{stamp}.csv"), csv.ToString());
        if (_sqlCensus.HasSamples)
        {
            await File.WriteAllTextAsync(Path.Combine(dir, $"chat-send-{VariantName}-{stamp}-sql.md"), _sqlCensus.Render());
        }
        TestContext.Out.WriteLine($"artifacts written to {dir}");
    }

    internal static double P(List<double> values, int percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private sealed class LegSamples
    {
        public List<double> Ms { get; } = [];
        public List<long> NonQuery { get; } = [];
        public List<long> Reader { get; } = [];
        public List<long> Scalar { get; } = [];
        public List<long> Opened { get; } = [];

        public void Add(double ms, (long nonQuery, long reader, long scalar, long opened) after,
            (long nonQuery, long reader, long scalar, long opened) before)
        {
            Ms.Add(ms);
            NonQuery.Add(after.nonQuery - before.nonQuery);
            Reader.Add(after.reader - before.reader);
            Scalar.Add(after.scalar - before.scalar);
            Opened.Add(after.opened - before.opened);
        }
    }

    /// <summary>
    /// Statement census: when <c>ScopedConnectionFactory</c>'s slow-query threshold is lowered to
    /// zero (local diagnostic edit), every statement logs a "slow query" warning; this lists them
    /// per leg in execution order for the first measured iteration so round-trips can be counted.
    /// </summary>
    private sealed class SqlCensus
    {
        private readonly Dictionary<string, List<string>> _byLeg = new();
        public bool HasSamples => _byLeg.Count > 0;

        public void Harvest(string leg, Dictionary<LogEventLevel, List<LogEvent>> events)
        {
            if (_byLeg.ContainsKey(leg)) return;
            var statements = events.Values.SelectMany(v => v)
                .Where(e => e.MessageTemplate.Text.Contains("slow query", StringComparison.Ordinal))
                .Select(e => e.Properties.TryGetValue("query", out var q) && q is ScalarValue { Value: string text }
                    ? text.Replace("\n", " ").Replace("\r", " ").Trim()
                    : "?")
                .ToList();
            if (statements.Count > 0) _byLeg[leg] = statements;
        }

        public string Render()
        {
            var sb = new StringBuilder();
            foreach (var (leg, statements) in _byLeg.OrderBy(kv => kv.Key))
            {
                sb.AppendLine($"## {leg} — {statements.Count} statements");
                sb.AppendLine();
                for (var i = 0; i < statements.Count; i++)
                {
                    var stmt = statements[i];
                    sb.AppendLine($"{i + 1}. `{(stmt.Length > 220 ? stmt[..220] + "…" : stmt)}`");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Collects every numeric <c>…Ms</c> property from log lines tagged <c>[…Timing]</c>, keyed by
    /// message template, so server-side phase timings can be read next to the leg wall clocks.
    /// </summary>
    private sealed class TimingLineAggregator
    {
        private readonly Dictionary<string, Dictionary<string, List<double>>> _samples = new();

        public void Harvest(Dictionary<LogEventLevel, List<LogEvent>> events)
        {
            foreach (var e in events.Values.SelectMany(v => v))
            {
                var template = e.MessageTemplate.Text;
                var tag = template.IndexOf("Timing]", StringComparison.Ordinal);
                if (tag < 0) continue;

                var byProp = _samples.TryGetValue(template, out var d) ? d : _samples[template] = new();
                foreach (var (name, value) in e.Properties)
                {
                    if (!name.EndsWith("Ms", StringComparison.OrdinalIgnoreCase)) continue;
                    if (value is not ScalarValue { Value: var raw } || raw == null) continue;
                    if (!double.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ms)) continue;
                    (byProp.TryGetValue(name, out var list) ? list : byProp[name] = []).Add(ms);
                }
            }
        }

        public string Render()
        {
            if (_samples.Count == 0) return "(no [Timing] log lines captured)";
            var sb = new StringBuilder();
            sb.AppendLine("| server timing line | property | n | p50 ms | p95 ms | mean ms |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|");
            foreach (var (template, props) in _samples.OrderBy(kv => kv.Key))
            {
                var label = template.Length > 90 ? template[..90] + "…" : template;
                label = label.Replace("|", "\\|");
                foreach (var (prop, values) in props.OrderBy(kv => kv.Key))
                {
                    sb.AppendLine($"| {label} | {prop} | {values.Count} | {P(values, 50):F2} | {P(values, 95):F2} | {values.Average():F2} |");
                }
            }
            return sb.ToString();
        }
    }
}

/// <summary>Same profile with the production Debug default log level raised to Information.</summary>
[TestFixture, Explicit("perf benchmark, not a regression test")]
public class ChatSendBenchmarkInfoLogTests : ChatSendBenchmarkTests
{
    protected override string VariantName => "info-log";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Information" };
}
