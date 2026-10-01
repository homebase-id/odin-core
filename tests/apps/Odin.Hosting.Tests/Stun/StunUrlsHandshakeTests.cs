#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.Tests.LiveRelay;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.Stun;

/// <summary>
/// A host behind a load balancer tells its devices which names answer STUN through the socket
/// handshake, from Stun:PublicUrls. The device puts the list into its ICE servers as given.
/// </summary>
public class StunUrlsHandshakeTests
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
            testIdentities: [TestIdentities.Samwise]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [Test]
    public async Task Handshake_CarriesTheConfiguredStunUrls_InOrder()
    {
        var sam = TestIdentities.Samwise;
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        var appId = Guid.NewGuid();
        await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerSam, appId, TargetDrive.NewTargetDrive());
        var (appToken, appSecret) = await ownerSam.AppManager.RegisterAppClient(appId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = await LiveRelayTestHelpers.ConnectAppSocketAsync(sam.OdinId, appToken, cts.Token);

        EstablishConnectionResponse handshake = await LiveRelayTestHelpers.DoHandshakeAsync(socket, appSecret, new List<TargetDrive>(), cts.Token);

        Assert.That(handshake.StunUrls, Is.EqualTo(PublicUrls),
            $"handshake carried stunUrls [{string.Join(", ", handshake.StunUrls ?? [])}]");

        await LiveRelayTestHelpers.CloseQuietlyAsync(socket);
    }
}
