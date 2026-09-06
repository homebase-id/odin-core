using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Serilog.Events;

namespace Odin.Hosting.Tests._Universal.Outbox.Performance;

/// <summary>
/// Chat-send profile over the real wire: Kestrel on 8443 with the dev certificates, so Frodo's
/// outbox delivers to capi.sam over loopback HTTPS through the production peer HTTP client and
/// the production CAPI authentication handler. An in-process EventListener on the System.Net
/// event sources counts DNS lookups, connections and TLS handshakes per message, and idle probes
/// after long pauses show what the first message after silence pays.
/// </summary>
[Explicit("perf benchmark, not a regression test")]
public class TlsChatSendBenchmarkTests
{
    private WebScaffold _scaffold;

    private static int EnvInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

    private static int[] IdleProbeSeconds =>
        (Environment.GetEnvironmentVariable("ODIN_BENCH_IDLE_SECONDS") ?? "70,130")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(int.Parse).ToArray();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task SendTextMessage_OverLoopbackTls()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var warmup = EnvInt("ODIN_BENCH_WARMUP", 20);
        var iterations = EnvInt("ODIN_BENCH_ITERATIONS", 100);

        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var drive = TargetDrive.NewTargetDrive();
        await PrepareScenario(frodo, sam, drive);

        using var net = new NetEventRecorder();
        var conversationId = Guid.NewGuid();
        var rows = new List<IterationRow>();

        for (var i = 0; i < warmup + iterations; i++)
        {
            var row = await RunIteration(frodo, sam, drive, conversationId, net, $"#{i}");
            if (i >= warmup)
            {
                rows.Add(row);
            }
        }

        var probes = new List<IterationRow>();
        foreach (var idle in IdleProbeSeconds)
        {
            TestContext.Out.WriteLine($"idle {idle}s …");
            await Task.Delay(TimeSpan.FromSeconds(idle));
            probes.Add(await RunIteration(frodo, sam, drive, conversationId, net, $"after {idle}s idle"));
        }

        var report = Render(rows, probes, warmup, iterations);
        TestContext.Out.WriteLine(report);
        var dir = Environment.GetEnvironmentVariable("ODIN_BENCH_OUT") ?? TestContext.CurrentContext.WorkDirectory;
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(Path.Combine(dir, $"chat-send-tls-{stamp}.md"), report);
        await File.WriteAllTextAsync(Path.Combine(dir, $"chat-send-tls-{stamp}-events.txt"), net.Dump());
    }

    private async Task<IterationRow> RunIteration(OwnerApiClientRedux frodo, OwnerApiClientRedux sam, TargetDrive drive,
        Guid conversationId, NetEventRecorder net, string label)
    {
        var uniqueId = Guid.NewGuid();
        var metadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AccessControlList = AccessControlList.Connected,
            AppData = new UploadAppFileMetaData
            {
                FileType = 7878,
                UniqueId = uniqueId,
                GroupId = conversationId,
                UserDate = UnixTimeUtc.Now(),
                Tags = [],
                Content = "{\"message\":\"" + new string('x', 200) + "\",\"replyId\":null}",
            },
        };
        var transit = new TransitOptions
        {
            Recipients = [sam.Identity.OdinId],
            UseAppNotification = true,
            AppNotificationOptions = new AppNotificationOptions
            {
                AppId = Guid.Parse("2d781401-3804-4b57-b4aa-d8e4e2ef39f4"),
                TypeId = conversationId,
                TagId = uniqueId,
                UnEncryptedMessage = "New message",
            },
        };

        var row = new IterationRow { Label = label };

        var mark = net.Mark();
        var sw = Stopwatch.StartNew();
        var (upload, _) = await frodo.DriveRedux.UploadNewEncryptedMetadata(metadata, new StorageOptions { Drive = drive }, transit);
        row.UploadMs = sw.Elapsed.TotalMilliseconds;
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"{label}: upload {upload.StatusCode}");
        Assert.That(upload.Content.RecipientStatus[sam.Identity.OdinId], Is.EqualTo(TransferStatus.Enqueued));

        // The outbox worker runs on its own; poll tightly rather than with the 100 ms helper.
        sw.Restart();
        while (true)
        {
            var status = await frodo.DriveRedux.GetDriveStatus(drive);
            Assert.That(status.IsSuccessStatusCode, Is.True);
            if (status.Content.Outbox.TotalItems == 0) break;
            if (sw.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException($"{label}: outbox never drained");
            await Task.Delay(1);
        }
        row.DeliveryMs = sw.Elapsed.TotalMilliseconds;
        row.Net = net.Since(mark);

        sw.Restart();
        var inbox = await sam.DriveRedux.ProcessInbox(drive, batchSize: 100);
        row.InboxMs = sw.Elapsed.TotalMilliseconds;
        Assert.That(inbox.IsSuccessStatusCode, Is.True);
        Assert.That(inbox.Content.TotalItems, Is.EqualTo(0), $"{label}: inbox not empty");
        return row;
    }

    private static string Render(List<IterationRow> rows, List<IterationRow> probes, int warmup, int iterations)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Chat send over loopback TLS — {iterations} measured iterations ({warmup} warm-up)");
        sb.AppendLine();
        sb.AppendLine("| stage | p50 ms | p95 ms | min ms | mean ms |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        Stat("1-upload", rows.Select(r => r.UploadMs));
        Stat("2-delivery (upload return → outbox empty, 1 ms poll)", rows.Select(r => r.DeliveryMs));
        Stat("3-inbox", rows.Select(r => r.InboxMs));
        Stat("**end-to-end**", rows.Select(r => r.UploadMs + r.DeliveryMs + r.InboxMs));
        sb.AppendLine();
        sb.AppendLine("Per message, mean over measured iterations (peer = requests to `capi.*` hosts):");
        sb.AppendLine();
        sb.AppendLine("| peer requests | peer DNS lookups | peer connections | peer TLS handshakes | handshake ms (mean/max) | server-side handshakes | CAPI validate callbacks | other client requests |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
        sb.AppendLine(NetRow(rows.Select(r => r.Net).ToList()));
        sb.AppendLine();
        sb.AppendLine("### Idle probes (one message after a pause)");
        sb.AppendLine();
        sb.AppendLine("| probe | upload ms | delivery ms | inbox ms | peer DNS | peer connections | peer TLS handshakes | handshake ms | CAPI callbacks |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var p in probes)
        {
            sb.AppendLine($"| {p.Label} | {p.UploadMs:F1} | {p.DeliveryMs:F1} | {p.InboxMs:F1} | {p.Net.PeerDns} | {p.Net.PeerConnections} | {p.Net.PeerHandshakes} | {(p.Net.HandshakeMs.Count == 0 ? "-" : p.Net.HandshakeMs.Max().ToString("F1"))} | {p.Net.CapiCallbacks} |");
        }
        return sb.ToString();

        void Stat(string name, IEnumerable<double> values)
        {
            var v = values.ToList();
            sb.AppendLine($"| {name} | {P(v, 50):F2} | {P(v, 95):F2} | {v.Min():F2} | {v.Average():F2} |");
        }
    }

    private static string NetRow(List<NetSummary> nets)
    {
        double Mean(Func<NetSummary, double> f) => nets.Average(f);
        var hs = nets.SelectMany(n => n.HandshakeMs).ToList();
        var hsText = hs.Count == 0 ? "-" : $"{hs.Average():F1} / {hs.Max():F1}";
        return $"| {Mean(n => n.PeerRequests):F2} | {Mean(n => n.PeerDns):F2} | {Mean(n => n.PeerConnections):F2} | {Mean(n => n.PeerHandshakes):F2} | {hsText} | {Mean(n => n.ServerHandshakes):F2} | {Mean(n => n.CapiCallbacks):F2} | {Mean(n => n.OtherRequests):F2} |";
    }

    private static double P(List<double> values, int percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private sealed class IterationRow
    {
        public string Label = "";
        public double UploadMs, DeliveryMs, InboxMs;
        public NetSummary Net = new();
    }

    private sealed class NetSummary
    {
        public int PeerRequests, PeerDns, PeerConnections, PeerHandshakes, ServerHandshakes, CapiCallbacks, OtherRequests;
        public List<double> HandshakeMs = [];
    }

    private sealed record NetEvent(long Seq, DateTime At, string Source, string Name, Guid Activity, string Detail, string Host);

    /// <summary>
    /// Subscribes to the System.Net event sources for the whole process (both identities and the
    /// test client share it) and classifies events by target host so peer traffic can be told apart
    /// from the test client's own calls.
    /// </summary>
    private sealed class NetEventRecorder : EventListener
    {
        private readonly ConcurrentQueue<NetEvent> _events = new();
        private long _seq;
        private readonly List<EventSource> _pending = [];
        private bool _armed;

        public NetEventRecorder()
        {
            _armed = true;
            lock (_pending)
            {
                foreach (var source in _pending) Enable(source);
                _pending.Clear();
            }
        }

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (_armed) Enable(source);
            else lock (_pending) _pending.Add(source);
        }

        private void Enable(EventSource source)
        {
            switch (source.Name)
            {
                case "System.Net.Http":
                case "System.Net.Security":
                case "System.Net.NameResolution":
                case "System.Net.Sockets":
                    EnableEvents(source, EventLevel.Informational);
                    break;
                case "System.Threading.Tasks.TplEventSource":
                    EnableEvents(source, EventLevel.Informational, (EventKeywords)0x80); // TasksFlowActivityIds
                    break;
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventSource.Name == "System.Threading.Tasks.TplEventSource") return;
            var payload = e.Payload;
            var names = e.PayloadNames;
            string Get(string n)
            {
                if (payload == null || names == null) return "";
                var i = names.IndexOf(n);
                return i >= 0 && payload[i] != null ? payload[i].ToString() : "";
            }
            var host = e.EventName switch
            {
                "RequestStart" => Get("host"),
                "ConnectionEstablished" => Get("host"),
                "HandshakeStart" => Get("targetHost"),
                "ResolutionStart" => Get("hostNameOrAddress"),
                _ => "",
            };
            var detail = e.EventName switch
            {
                "RequestStart" => Get("pathAndQuery"),
                "HandshakeStart" => Get("isServer"),
                "RequestStop" => Get("statusCode"),
                _ => payload == null ? "" : string.Join(",", payload.Select(p => p?.ToString())),
            };
            _events.Enqueue(new NetEvent(Interlocked.Increment(ref _seq), e.TimeStamp, e.EventSource.Name, e.EventName ?? "", e.ActivityId, detail, host));
        }

        public long Mark() => Interlocked.Read(ref _seq);

        public NetSummary Since(long mark)
        {
            var events = _events.Where(x => x.Seq > mark).OrderBy(x => x.Seq).ToList();
            var s = new NetSummary();
            var openHandshakes = new Dictionary<Guid, (DateTime at, bool peer)>();
            var pendingPeer = new Queue<DateTime>();
            foreach (var ev in events)
            {
                var isPeer = ev.Host.StartsWith("capi.", StringComparison.OrdinalIgnoreCase);
                switch (ev.Source, ev.Name)
                {
                    case ("System.Net.Http", "RequestStart"):
                        if (isPeer) s.PeerRequests++;
                        else if (ev.Detail.Contains("/capi/", StringComparison.OrdinalIgnoreCase)) s.CapiCallbacks++;
                        else s.OtherRequests++;
                        break;
                    case ("System.Net.NameResolution", "ResolutionStart"):
                        if (isPeer) s.PeerDns++;
                        break;
                    case ("System.Net.Http", "ConnectionEstablished"):
                        if (isPeer) s.PeerConnections++;
                        break;
                    case ("System.Net.Security", "HandshakeStart"):
                        if (ev.Detail == "True") s.ServerHandshakes++;
                        else
                        {
                            if (isPeer) s.PeerHandshakes++;
                            if (ev.Activity != Guid.Empty) openHandshakes[ev.Activity] = (ev.At, isPeer);
                            else if (isPeer) pendingPeer.Enqueue(ev.At);
                        }
                        break;
                    case ("System.Net.Security", "HandshakeStop"):
                        if (ev.Activity != Guid.Empty && openHandshakes.Remove(ev.Activity, out var open))
                        {
                            if (open.peer) s.HandshakeMs.Add((ev.At - open.at).TotalMilliseconds);
                        }
                        else if (pendingPeer.TryDequeue(out var at))
                        {
                            s.HandshakeMs.Add((ev.At - at).TotalMilliseconds);
                        }
                        break;
                }
            }
            return s;
        }

        public string Dump()
        {
            var sb = new StringBuilder();
            foreach (var ev in _events.OrderBy(x => x.Seq))
            {
                sb.Append(CultureInfo.InvariantCulture, $"{ev.Seq}\t{ev.At:HH:mm:ss.ffffff}\t{ev.Source}\t{ev.Name}\t{ev.Host}\t{ev.Detail}\t{ev.Activity}\n");
            }
            return sb.ToString();
        }
    }

    private static async Task PrepareScenario(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TargetDrive drive)
    {
        var recipientDrive = await recipient.DriveManager.CreateDrive(drive, "chat on recipient", "", false, false, false);
        Assert.That(recipientDrive.IsSuccessStatusCode, Is.True);
        var senderDrive = await sender.DriveManager.CreateDrive(drive, "chat on sender", "", false, false, false);
        Assert.That(senderDrive.IsSuccessStatusCode, Is.True);

        var circleId = Guid.NewGuid();
        var circle = await recipient.Network.CreateCircle(circleId, "chat circle", new PermissionSetGrantRequest
        {
            Drives =
            [
                new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Write },
                },
            ],
        });
        Assert.That(circle.IsSuccessStatusCode, Is.True);

        await sender.Connections.SendConnectionRequest(recipient.Identity.OdinId, new List<GuidId>());
        await recipient.Connections.AcceptConnectionRequest(sender.Identity.OdinId, new List<GuidId> { circleId });
    }
}
