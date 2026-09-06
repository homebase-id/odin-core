#nullable enable
using System.Buffers;
using System.Net;
using NUnit.Framework;
using Odin.Hosting.Kestrel;

namespace Odin.Hosting.Tests.Kestrel;

public class ProxyProtocolParserTests
{
    private static readonly IPAddress Client = IPAddress.Parse("203.0.113.45");
    private static readonly IPAddress Client6 = IPAddress.Parse("2001:db8::45");
    private static readonly IPAddress Balancer = IPAddress.Parse("10.0.0.7");

    [Test]
    public void V2_Inet_ParsesAddressesAndLeavesTheRest()
    {
        var header = ProxyProtocolTestSupport.V2Header(Client, 40000, Balancer, 443);
        var tls = new byte[] { 0x16, 0x03, 0x01, 0x00, 0xf1 };
        var buffer = new ReadOnlySequence<byte>([.. header, .. tls]);

        var status = ProxyProtocolParser.TryParse(buffer, out var parsed, out var consumed);

        Assert.That(status, Is.EqualTo(ProxyProtocolParseStatus.Success));
        Assert.That(parsed!.Version, Is.EqualTo(2));
        Assert.That(parsed.Source, Is.EqualTo(new IPEndPoint(Client, 40000)));
        Assert.That(parsed.Destination, Is.EqualTo(new IPEndPoint(Balancer, 443)));
        Assert.That(buffer.Slice(consumed).ToArray(), Is.EqualTo(tls), "only the header may be consumed");
    }

    [Test]
    public void V2_Inet6_Parses()
    {
        var buffer = new ReadOnlySequence<byte>(ProxyProtocolTestSupport.V2Header(Client6, 1, IPAddress.IPv6Loopback, 2));
        var status = ProxyProtocolParser.TryParse(buffer, out var parsed, out _);
        Assert.That(status, Is.EqualTo(ProxyProtocolParseStatus.Success));
        Assert.That(parsed!.Source!.Address, Is.EqualTo(Client6));
    }

    [Test]
    public void V2_Local_HasNoAddresses()
    {
        var buffer = new ReadOnlySequence<byte>(ProxyProtocolTestSupport.V2LocalHeader());
        var status = ProxyProtocolParser.TryParse(buffer, out var parsed, out var consumed);
        Assert.That(status, Is.EqualTo(ProxyProtocolParseStatus.Success));
        Assert.That(parsed!.HasAddresses, Is.False);
        Assert.That(buffer.Slice(consumed).Length, Is.EqualTo(0));
    }

    [Test]
    public void V2_WithTlvs_SkipsThem()
    {
        var header = ProxyProtocolTestSupport.V2Header(Client, 40000, Balancer, 443);
        var tlv = new byte[] { 0x01, 0x00, 0x02, 0x68, 0x32 }; // PP2_TYPE_ALPN "h2"
        header[15] = (byte)(12 + tlv.Length);
        var buffer = new ReadOnlySequence<byte>([.. header, .. tlv, 0x16]);

        var status = ProxyProtocolParser.TryParse(buffer, out _, out var consumed);
        Assert.That(status, Is.EqualTo(ProxyProtocolParseStatus.Success));
        Assert.That(buffer.Slice(consumed).ToArray(), Is.EqualTo(new byte[] { 0x16 }));
    }

    [Test]
    public void V2_Truncated_NeedsMoreData()
    {
        var header = ProxyProtocolTestSupport.V2Header(Client, 40000, Balancer, 443);
        for (var length = 1; length < header.Length; length++)
        {
            var status = ProxyProtocolParser.TryParse(new ReadOnlySequence<byte>(header[..length]), out _, out _);
            Assert.That(status, Is.EqualTo(ProxyProtocolParseStatus.NeedMoreData), $"prefix of {length} bytes");
        }
    }

    [Test]
    public void V1_Tcp4_Parses()
    {
        var buffer = new ReadOnlySequence<byte>([.. ProxyProtocolTestSupport.V1Header(Client, 40000, Balancer, 443), 0x16]);
        var status = ProxyProtocolParser.TryParse(buffer, out var parsed, out var consumed);
        Assert.That(status, Is.EqualTo(ProxyProtocolParseStatus.Success));
        Assert.That(parsed!.Version, Is.EqualTo(1));
        Assert.That(parsed.Source, Is.EqualTo(new IPEndPoint(Client, 40000)));
        Assert.That(buffer.Slice(consumed).ToArray(), Is.EqualTo(new byte[] { 0x16 }));
    }

    [Test]
    public void V1_Unknown_HasNoAddresses()
    {
        var buffer = new ReadOnlySequence<byte>("PROXY UNKNOWN\r\n"u8.ToArray());
        var status = ProxyProtocolParser.TryParse(buffer, out var parsed, out _);
        Assert.That(status, Is.EqualTo(ProxyProtocolParseStatus.Success));
        Assert.That(parsed!.HasAddresses, Is.False);
    }

    [Test]
    public void V1_WithoutLineEnding_NeedsMoreData_ThenInvalidPast107Bytes()
    {
        Assert.That(ProxyProtocolParser.TryParse(new ReadOnlySequence<byte>("PROXY TCP4 203.0"u8.ToArray()), out _, out _),
            Is.EqualTo(ProxyProtocolParseStatus.NeedMoreData));

        var overlong = new byte[120];
        System.Array.Fill(overlong, (byte)'P');
        "PROXY "u8.CopyTo(overlong);
        Assert.That(ProxyProtocolParser.TryParse(new ReadOnlySequence<byte>(overlong), out _, out _),
            Is.EqualTo(ProxyProtocolParseStatus.Invalid));
    }

    [TestCase(new byte[] { 0x16, 0x03, 0x01 }, TestName = "TLS ClientHello is not a header")]
    [TestCase(new byte[] { (byte)'G', (byte)'E', (byte)'T', (byte)' ' }, TestName = "HTTP request is not a header")]
    [TestCase(new byte[] { (byte)'P', (byte)'R', (byte)'O', (byte)'X', (byte)'Y', (byte)' ', (byte)'X', (byte)'\r', (byte)'\n' }, TestName = "Bad v1 family")]
    public void Garbage_IsInvalid(byte[] bytes)
    {
        Assert.That(ProxyProtocolParser.TryParse(new ReadOnlySequence<byte>(bytes), out _, out _),
            Is.EqualTo(ProxyProtocolParseStatus.Invalid));
    }

    [Test]
    public void V2_WrongVersionNibble_IsInvalid()
    {
        var header = ProxyProtocolTestSupport.V2Header(Client, 1, Balancer, 2);
        header[12] = 0x31;
        Assert.That(ProxyProtocolParser.TryParse(new ReadOnlySequence<byte>(header), out _, out _),
            Is.EqualTo(ProxyProtocolParseStatus.Invalid));
    }

    [Test]
    public void TrustedProxyCheck_HandlesIPv4MappedPeers()
    {
        var trusted = new[] { IPNetwork.Parse("127.0.0.0/8") };
        Assert.That(ProxyProtocolConnectionMiddleware.IsTrusted(IPAddress.Parse("::ffff:127.0.0.1"), trusted), Is.True);
        Assert.That(ProxyProtocolConnectionMiddleware.IsTrusted(IPAddress.Parse("10.0.0.1"), trusted), Is.False);
        Assert.That(ProxyProtocolConnectionMiddleware.IsTrusted(IPAddress.IPv6Loopback, trusted), Is.False);
    }
}
