#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Test.Helpers.Stun;
using Serilog.Events;

namespace Odin.Hosting.Tests.Stun;

/// <summary>
/// The real host answers a Binding request. Covers the wiring the Odin.Services unit tests
/// cannot: config parsing from the environment, registration as a system background service,
/// and the conditional start. WebScaffold binds the responder to an ephemeral loopback port.
/// </summary>
public class StunResponderHostTests
{
    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo]);
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
}
