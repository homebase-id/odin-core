#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests._Universal.DriveTests;

namespace Odin.Hosting.Tests.LoadBalancer;

/// <summary>
/// Does a WebSocket client connected to one node hear about an event processed on the other?
///
/// A client's socket lives in the process it connected to (<c>SharedDeviceSocketCollection</c> is a
/// per-process singleton), while the event may be processed anywhere, so the two are routinely on
/// different nodes behind a balancer. <c>AppNotificationDispatcher</c> is built for that: it never
/// writes to sockets directly, it publishes to <c>ITenantPubSub</c> and every node subscribes and
/// writes to whatever sockets it holds. With Redis enabled that channel crosses nodes. These tests
/// check whether that actually happens end to end, which the earlier cluster probes did not cover.
///
/// Needs the same two live nodes as <see cref="LoadBalancerProbeTests"/>: node A on 8443, node B on
/// 8444, one Postgres, one Redis.
/// </summary>
[Explicit("requires two live nodes sharing postgres + redis; see docs/load-balancing.md")]
public class WebSocketFanOutProbeTests
{
    private const int NodeA = 8443;
    private const int NodeB = 8444;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly OdinId Frodo = (OdinId)"frodo.dotyou.cloud";

    private ClientAuthenticationToken _token = null!;
    private SensitiveByteArray _sharedSecret = null!;

    [OneTimeSetUp]
    public async Task LoginOnNodeA()
    {
        var (token, secret) = await LoadBalancerProbeTests.LoginForProbesAsync(Frodo, NodeA);
        _token = token;
        _sharedSecret = secret;
    }

    /// <summary>Control: socket and event on the SAME node. If this fails the harness is wrong,
    /// not the cluster, so it must be read before believing any cross-node result.</summary>
    [Test]
    public async Task SameNode_SocketReceivesFileAdded()
    {
        var drive = await CreateDriveAsync(NodeA);
        await using var socket = await ConnectAsync(NodeA, drive);

        await UploadAsync(NodeA, drive);

        var received = await socket.WaitForAsync(ClientNotificationType.FileAdded, Patience);
        Assert.That(received, Is.Not.Null,
            "a socket did not receive a file event raised on the very node it is connected to; the harness is broken");
    }

    /// <summary>The question this suite exists for: socket on node B, event on node A.</summary>
    [Test]
    public async Task CrossNode_SocketOnBReceivesEventFromA()
    {
        var drive = await CreateDriveAsync(NodeA);
        await using var socket = await ConnectAsync(NodeB, drive);

        await UploadAsync(NodeA, drive);

        var received = await socket.WaitForAsync(ClientNotificationType.FileAdded, Patience);
        Assert.That(received, Is.Not.Null,
            "a socket on node B never heard about a file uploaded through node A; notifications do not cross nodes, " +
            "so behind a balancer a client hears events only when it happens to be connected to the node that " +
            "processed them");
    }

    /// <summary>Both directions, so a pass is not an artefact of which node happens to publish.</summary>
    [Test]
    public async Task CrossNode_SocketOnAReceivesEventFromB()
    {
        var drive = await CreateDriveAsync(NodeA);
        await using var socket = await ConnectAsync(NodeA, drive);

        await UploadAsync(NodeB, drive);

        var received = await socket.WaitForAsync(ClientNotificationType.FileAdded, Patience);
        Assert.That(received, Is.Not.Null, "a socket on node A never heard about a file uploaded through node B");
    }

    /// <summary>One socket per node: every socket hears the event exactly once, so the fan-out
    /// neither drops a node nor delivers twice to the node that published.</summary>
    [Test]
    public async Task EverySocketHearsTheEventExactlyOnce()
    {
        var drive = await CreateDriveAsync(NodeA);
        await using var onA = await ConnectAsync(NodeA, drive);
        await using var onB = await ConnectAsync(NodeB, drive);

        await UploadAsync(NodeA, drive);

        Assert.That(await onA.WaitForAsync(ClientNotificationType.FileAdded, Patience), Is.Not.Null, "socket on A missed it");
        Assert.That(await onB.WaitForAsync(ClientNotificationType.FileAdded, Patience), Is.Not.Null, "socket on B missed it");

        // Let any duplicate arrive before counting.
        await Task.Delay(2000);
        Assert.That(onA.CountOf(ClientNotificationType.FileAdded), Is.EqualTo(1), "node A delivered the event more than once");
        Assert.That(onB.CountOf(ClientNotificationType.FileAdded), Is.EqualTo(1), "node B delivered the event more than once");
    }

    /// <summary>The cross-node path must still respect the socket's drive subscription; a fan-out
    /// that forwards everything would leak activity on drives the client never asked about.</summary>
    [Test]
    public async Task CrossNode_DoesNotDeliverEventsForUnsubscribedDrives()
    {
        var watched = await CreateDriveAsync(NodeA);
        var other = await CreateDriveAsync(NodeA);
        await using var socket = await ConnectAsync(NodeB, watched);

        await UploadAsync(NodeA, other);

        var leaked = await socket.WaitForAsync(ClientNotificationType.FileAdded, TimeSpan.FromSeconds(6));
        Assert.That(leaked, Is.Null, "a socket received an event for a drive it never subscribed to");
    }

    // ---------- plumbing ----------

    private async Task<NodeWebSocketListener> ConnectAsync(int port, TargetDrive drive)
    {
        var listener = new NodeWebSocketListener();
        await listener.ConnectAsync(Frodo, port, _token, _sharedSecret,
            new EstablishConnectionOptions { Drives = [drive] });
        return listener;
    }

    private async Task<TargetDrive> CreateDriveAsync(int port)
    {
        var drive = TargetDrive.NewTargetDrive();
        var created = await LoadBalancerProbeTests.DriveManagerForProbes(Frodo, port, _token, _sharedSecret)
            .CreateDrive(new CreateDriveRequest
            {
                TargetDrive = drive,
                Name = "ws probe " + drive.Alias,
                Metadata = "",
                AllowAnonymousReads = false,
            });
        Assert.That(created.IsSuccessStatusCode, Is.True, $"create drive on {port} failed: {created.StatusCode}");
        return drive;
    }

    private async Task UploadAsync(int port, TargetDrive drive)
    {
        var metadata = SampleMetadataData.Create(fileType: 5150, acl: AccessControlList.OwnerOnly);
        metadata.AppData.Content = "ws probe " + Guid.NewGuid();
        var upload = await new UniversalDriveApiClient(Frodo, LoadBalancerProbeTests.FactoryForProbes(port, _token, _sharedSecret))
            .UploadNewMetadata(drive, metadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload on {port} failed: {upload.StatusCode}");
    }
}
