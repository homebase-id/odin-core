#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._V2.Tests.LiveRelay;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Test.Helpers.Stun;
using Serilog.Events;

namespace Odin.Hosting.Tests.Stun;

/// <summary>
/// The real host with STUN configured from the environment: the responder answers a Binding
/// request on its ephemeral loopback port, and the socket handshake hands devices the configured
/// public URLs as given. Covers the wiring the Odin.Services unit tests cannot: config parsing,
/// registration as a system background service, the conditional start, and the handshake field.
/// </summary>
public class StunResponderHostTests
{
    private static readonly string[] PublicUrls =
    [
        "stun:stun1-1.eu.example:3478",
        "stun:stun1-2.eu.example:3478",
    ];

    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(
            envOverrides: new Dictionary<string, string>
            {
                ["Stun__PublicUrls__0"] = PublicUrls[0],
                ["Stun__PublicUrls__1"] = PublicUrls[1],
            },
            testIdentities: [TestIdentities.Frodo]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task HostStartsTheResponder_AndItAnswersABindingRequest()
    {
        var info = _scaffold.GetLogEvents()[LogEventLevel.Information];
        var listening = info.FirstOrDefault(e => e.MessageTemplate.Text.StartsWith("STUN responder listening on"));
        Assert.That(listening, Is.Not.Null,
            "no 'STUN responder listening on' event; Information events were:\n" +
            string.Join("\n", info.Select(e => e.RenderMessage())));

        // The endpoint is logged as a scalar; its rendered form is the IPEndPoint's ToString.
        var responder = IPEndPoint.Parse(listening!.Properties["endpoint"].ToString().Trim('"'));
        Assert.That(responder.Port, Is.Not.Zero, $"expected an ephemeral port, host bound {responder}");

        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var transactionId = ByteArrayUtil.GetRndByteArray(12);
        var request = StunTestMessages.BareBindingRequest(transactionId);

        await client.SendAsync(request, request.Length, responder);
        var reply = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(reply.Buffer[8..20], Is.EqualTo(transactionId),
            $"transaction id must be echoed; reply was {Convert.ToHexString(reply.Buffer)}");
        Assert.That(StunTestMessages.TryReadXorMappedAddress(reply.Buffer, out var mapped), Is.True,
            $"could not decode {Convert.ToHexString(reply.Buffer)}");
        Assert.That(mapped, Is.EqualTo(client.Client.LocalEndPoint),
            $"host said {mapped}, client socket is {client.Client.LocalEndPoint}");
    }

    [Test]
    public async Task Handshake_CarriesTheConfiguredStunUrls_InOrder()
    {
        var frodo = TestIdentities.Frodo;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var app = await ownerFrodo.AppManager.RegisterAppAndClient(Guid.NewGuid(),
            new PermissionSetGrantRequest { PermissionSet = new PermissionSet(PermissionKeys.ReadConnections) });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = await LiveRelayTestHelpers.ConnectAppSocketAsync(frodo.OdinId, app.ToAuthenticationToken(), cts.Token);

        var handshake = await LiveRelayTestHelpers.DoHandshakeAsync(socket, app.SharedSecret.GetKey(), new List<TargetDrive>(), cts.Token);

        Assert.That(handshake.StunUrls, Is.EqualTo(PublicUrls));

        await LiveRelayTestHelpers.CloseQuietlyAsync(socket);
    }
}
