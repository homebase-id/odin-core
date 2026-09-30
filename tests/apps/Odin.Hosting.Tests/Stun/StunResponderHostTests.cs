#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Stun;
using Serilog.Events;

namespace Odin.Hosting.Tests.Stun;

/// <summary>
/// The real host, booted with the STUN responder enabled on an ephemeral loopback port, answers a
/// Binding request. Covers the wiring the Odin.Services unit tests cannot: config parsing from
/// the environment, registration as a system background service, and the conditional start.
/// Every other scaffold runs with <c>Stun__Enabled=false</c>.
/// </summary>
public class StunResponderHostTests
{
    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(
            envOverrides: new Dictionary<string, string>
            {
                ["Stun__Enabled"] = "true",
                ["Stun__BindAddress"] = "127.0.0.1",
                ["Stun__Port"] = "0",
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
        var info = _scaffold.GetLogEvents()[LogEventLevel.Information].Select(e => e.RenderMessage()).ToList();

        Assert.That(info, Has.Some.EqualTo("STUN responder enabled: True"),
            "startup must log the flag; Information events were:\n" + string.Join("\n", info));

        // RenderMessage quotes the endpoint (Serilog renders it as a string scalar).
        var listening = info.Select(m => Regex.Match(m, @"^STUN responder listening on ""?([^""\s]+)""?$"))
            .FirstOrDefault(m => m.Success);
        Assert.That(listening, Is.Not.Null,
            "no 'STUN responder listening on' event; Information events were:\n" + string.Join("\n", info));
        var responder = IPEndPoint.Parse(listening!.Groups[1].Value);
        Assert.That(responder.Port, Is.Not.Zero, $"expected an ephemeral port, host bound {responder}");

        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var transactionId = Guid.NewGuid().ToByteArray()[..12];
        var request = new byte[StunBindingCodec.HeaderLength];
        request[1] = 0x01;
        request[4] = 0x21;
        request[5] = 0x12;
        request[6] = 0xa4;
        request[7] = 0x42;
        transactionId.CopyTo(request, 8);

        await client.SendAsync(request, request.Length, responder);
        var reply = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(reply.Buffer[8..20], Is.EqualTo(transactionId),
            $"transaction id must be echoed; reply was {Convert.ToHexString(reply.Buffer)}");
        Assert.That(StunBindingCodec.TryReadXorMappedAddress(reply.Buffer, out var mapped), Is.True,
            $"could not decode {Convert.ToHexString(reply.Buffer)}");
        Assert.That(mapped, Is.EqualTo(client.Client.LocalEndPoint),
            $"host said {mapped}, client socket is {client.Client.LocalEndPoint}");
    }
}
