#nullable enable
using System;
using System.Linq;
using System.Net;
using NUnit.Framework;
using Odin.Services.Stun;
using Odin.Test.Helpers.Stun;

namespace Odin.Services.Tests.Stun;

/// <summary>
/// Golden bytes are the RFC 5769 test vectors (§2.1 request, §2.2 IPv4 response, §2.3 IPv6
/// response). Their transaction id is b7e7a701 bc34d686 fa87dfae and the mapped address is
/// 192.0.2.1:32853 / [2001:db8:1234:5678:11:2233:4455:6677]:32853.
/// </summary>
public class StunBindingCodecTests
{
    private static readonly byte[] TransactionId =
        [0xb7, 0xe7, 0xa7, 0x01, 0xbc, 0x34, 0xd6, 0x86, 0xfa, 0x87, 0xdf, 0xae];

    // RFC 5769 §2.1: a Binding request with SOFTWARE, PRIORITY, ICE-CONTROLLED, USERNAME,
    // MESSAGE-INTEGRITY and FINGERPRINT. 20-byte header + 88 attribute bytes.
    private static readonly byte[] Rfc5769Request =
    [
        0x00, 0x01, 0x00, 0x58, 0x21, 0x12, 0xa4, 0x42,
        0xb7, 0xe7, 0xa7, 0x01, 0xbc, 0x34, 0xd6, 0x86, 0xfa, 0x87, 0xdf, 0xae,
        0x80, 0x22, 0x00, 0x10, 0x53, 0x54, 0x55, 0x4e, 0x20, 0x74, 0x65, 0x73,
        0x74, 0x20, 0x63, 0x6c, 0x69, 0x65, 0x6e, 0x74,
        0x00, 0x24, 0x00, 0x04, 0x6e, 0x00, 0x01, 0xff,
        0x80, 0x29, 0x00, 0x08, 0x93, 0x2f, 0xf9, 0xb1, 0x51, 0x26, 0x3b, 0x36,
        0x00, 0x06, 0x00, 0x09, 0x65, 0x76, 0x74, 0x6a, 0x3a, 0x68, 0x36, 0x76,
        0x59, 0x20, 0x20, 0x20,
        0x00, 0x08, 0x00, 0x14, 0x9a, 0xea, 0xa7, 0x0c, 0xbf, 0xd8, 0xcb, 0x56,
        0x78, 0x1e, 0xf2, 0xb5, 0xb2, 0xd3, 0xf2, 0x49, 0xc1, 0xb5, 0x71, 0xa2,
        0x80, 0x28, 0x00, 0x04, 0xe5, 0x7a, 0x3b, 0xcf,
    ];

    // RFC 5769 §2.2: Binding success with SOFTWARE "test vector", XOR-MAPPED-ADDRESS (IPv4),
    // MESSAGE-INTEGRITY, FINGERPRINT.
    private static readonly byte[] Rfc5769IPv4Response =
    [
        0x01, 0x01, 0x00, 0x3c, 0x21, 0x12, 0xa4, 0x42,
        0xb7, 0xe7, 0xa7, 0x01, 0xbc, 0x34, 0xd6, 0x86, 0xfa, 0x87, 0xdf, 0xae,
        0x80, 0x22, 0x00, 0x0b, 0x74, 0x65, 0x73, 0x74, 0x20, 0x76, 0x65, 0x63,
        0x74, 0x6f, 0x72, 0x20,
        0x00, 0x20, 0x00, 0x08, 0x00, 0x01, 0xa1, 0x47, 0xe1, 0x12, 0xa6, 0x43,
        0x00, 0x08, 0x00, 0x14, 0x2b, 0x91, 0xf5, 0x99, 0xfd, 0x9e, 0x90, 0xc3,
        0x8c, 0x74, 0x89, 0xf9, 0x2a, 0xf9, 0xba, 0x53, 0xf0, 0x6b, 0xe7, 0xd7,
        0x80, 0x28, 0x00, 0x04, 0xc0, 0x7d, 0x4c, 0x96,
    ];

    // RFC 5769 §2.3: same, IPv6.
    private static readonly byte[] Rfc5769IPv6Response =
    [
        0x01, 0x01, 0x00, 0x48, 0x21, 0x12, 0xa4, 0x42,
        0xb7, 0xe7, 0xa7, 0x01, 0xbc, 0x34, 0xd6, 0x86, 0xfa, 0x87, 0xdf, 0xae,
        0x80, 0x22, 0x00, 0x0b, 0x74, 0x65, 0x73, 0x74, 0x20, 0x76, 0x65, 0x63,
        0x74, 0x6f, 0x72, 0x20,
        0x00, 0x20, 0x00, 0x14, 0x00, 0x02, 0xa1, 0x47, 0x01, 0x13, 0xa9, 0xfa,
        0xa5, 0xd3, 0xf1, 0x79, 0xbc, 0x25, 0xf4, 0xb5, 0xbe, 0xd2, 0xb9, 0xd9,
        0x00, 0x08, 0x00, 0x14, 0xa3, 0x82, 0x95, 0x4e, 0x4b, 0xe6, 0x7b, 0xf1,
        0x17, 0x84, 0xc9, 0x7c, 0x82, 0x92, 0xc2, 0x75, 0xbf, 0xe3, 0xed, 0x41,
        0x80, 0x28, 0x00, 0x04, 0xc8, 0xfb, 0x0b, 0x4c,
    ];

    private static readonly IPEndPoint Rfc5769IPv4Endpoint = new(IPAddress.Parse("192.0.2.1"), 32853);
    private static readonly IPEndPoint Rfc5769IPv6Endpoint =
        new(IPAddress.Parse("2001:db8:1234:5678:11:2233:4455:6677"), 32853);

    private static byte[] BareBindingRequest(ReadOnlySpan<byte> transactionId) =>
        StunTestMessages.BareBindingRequest(transactionId);

    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    // --- parsing ---

    [Test]
    public void Rfc5769Request_IsABindingRequest_AndAttributesAreNotInspected()
    {
        var status = StunBindingCodec.TryParseBindingRequest(Rfc5769Request, out var tid);

        Assert.That(status, Is.EqualTo(StunParseStatus.BindingRequest));
        Assert.That(tid.ToArray(), Is.EqualTo(TransactionId), $"transaction id was {Hex(tid)}");
    }

    [Test]
    public void BareRequest_IsABindingRequest()
    {
        var status = StunBindingCodec.TryParseBindingRequest(BareBindingRequest(TransactionId), out var tid);

        Assert.That(status, Is.EqualTo(StunParseStatus.BindingRequest));
        Assert.That(tid.ToArray(), Is.EqualTo(TransactionId), $"transaction id was {Hex(tid)}");
    }

    [Test]
    public void EveryTruncatedHeader_IsNotStun()
    {
        var request = BareBindingRequest(TransactionId);
        for (var length = 0; length < request.Length; length++)
        {
            var status = StunBindingCodec.TryParseBindingRequest(request.AsSpan(0, length), out _);
            Assert.That(status, Is.EqualTo(StunParseStatus.NotStun), $"{length}-byte prefix gave {status}");
        }
    }

    [TestCase(0x40, Description = "top bits 01")]
    [TestCase(0x80, Description = "top bits 10")]
    [TestCase(0xC0, Description = "top bits 11")]
    public void TopTwoBitsSet_IsNotStun(int firstByte)
    {
        var request = BareBindingRequest(TransactionId);
        request[0] = (byte)firstByte;

        var status = StunBindingCodec.TryParseBindingRequest(request, out _);

        Assert.That(status, Is.EqualTo(StunParseStatus.NotStun), $"first byte 0x{firstByte:X2} gave {status}");
    }

    [Test]
    public void WrongMagicCookie_IsNotStun()
    {
        var request = BareBindingRequest(TransactionId);
        request[7] = 0x43;

        var status = StunBindingCodec.TryParseBindingRequest(request, out _);

        Assert.That(status, Is.EqualTo(StunParseStatus.NotStun), $"got {status}");
    }

    [Test]
    public void MessageLengthLongerThanDatagram_IsBadLength()
    {
        var request = BareBindingRequest(TransactionId);
        request[3] = 4; // claims 4 attribute bytes; there are none

        var status = StunBindingCodec.TryParseBindingRequest(request, out _);

        Assert.That(status, Is.EqualTo(StunParseStatus.BadLength), $"got {status}");
    }

    [Test]
    public void MessageLengthShorterThanDatagram_IsBadLength()
    {
        var request = BareBindingRequest(TransactionId).Concat(new byte[] { 0, 0, 0, 0 }).ToArray();
        // header still says 0 attribute bytes but the datagram has 4 trailing bytes

        var status = StunBindingCodec.TryParseBindingRequest(request, out _);

        Assert.That(status, Is.EqualTo(StunParseStatus.BadLength), $"got {status}");
    }

    [Test]
    public void MessageLengthNotMultipleOfFour_IsBadLength()
    {
        var request = BareBindingRequest(TransactionId).Concat(new byte[] { 0, 0, 0 }).ToArray();
        request[3] = 3;

        var status = StunBindingCodec.TryParseBindingRequest(request, out _);

        Assert.That(status, Is.EqualTo(StunParseStatus.BadLength), $"got {status}");
    }

    [TestCase(0x0011, Description = "Binding indication")]
    [TestCase(0x0101, Description = "Binding success response")]
    [TestCase(0x0111, Description = "Binding error response")]
    [TestCase(0x0003, Description = "TURN Allocate request")]
    [TestCase(0x0008, Description = "TURN CreatePermission request")]
    public void OtherMessageTypes_AreNotBindingRequests(int messageType)
    {
        var request = BareBindingRequest(TransactionId);
        request[0] = (byte)(messageType >> 8);
        request[1] = (byte)messageType;

        var status = StunBindingCodec.TryParseBindingRequest(request, out _);

        Assert.That(status, Is.EqualTo(StunParseStatus.NotBindingRequest), $"type 0x{messageType:X4} gave {status}");
    }

    [Test]
    public void MaxDatagram_WithConsistentLength_IsParsed()
    {
        var datagram = new byte[StunBindingCodec.MaxDatagramLength];
        BareBindingRequest(TransactionId).CopyTo(datagram, 0);
        var attributeBytes = StunBindingCodec.MaxDatagramLength - StunBindingCodec.HeaderLength;
        datagram[2] = (byte)(attributeBytes >> 8);
        datagram[3] = (byte)attributeBytes;

        var status = StunBindingCodec.TryParseBindingRequest(datagram, out _);

        Assert.That(status, Is.EqualTo(StunParseStatus.BindingRequest), $"got {status}");
    }

    // --- encoding ---

    [Test]
    public void WriteBindingSuccess_IPv4_MatchesRfc5769()
    {
        var buffer = new byte[StunBindingCodec.MaxResponseLength];

        var length = StunBindingCodec.WriteBindingSuccess(buffer, TransactionId,
            Rfc5769IPv4Endpoint.Address, Rfc5769IPv4Endpoint.Port);

        var expected = Rfc5769IPv4Response[..StunBindingCodec.HeaderLength]
            .Concat(Rfc5769IPv4Response[36..48]) // the XOR-MAPPED-ADDRESS attribute of the RFC's response
            .ToArray();
        expected[3] = 0x0c; // ours carries only that one attribute
        Assert.That(length, Is.EqualTo(32));
        Assert.That(buffer[..length], Is.EqualTo(expected),
            $"got {Hex(buffer.AsSpan(0, length))}\nexpected {Hex(expected)}");
    }

    [Test]
    public void WriteBindingSuccess_IPv6_MatchesRfc5769()
    {
        var buffer = new byte[StunBindingCodec.MaxResponseLength];

        var length = StunBindingCodec.WriteBindingSuccess(buffer, TransactionId,
            Rfc5769IPv6Endpoint.Address, Rfc5769IPv6Endpoint.Port);

        var expected = Rfc5769IPv6Response[..StunBindingCodec.HeaderLength]
            .Concat(Rfc5769IPv6Response[36..60])
            .ToArray();
        expected[3] = 0x18;
        Assert.That(length, Is.EqualTo(44));
        Assert.That(buffer[..length], Is.EqualTo(expected),
            $"got {Hex(buffer.AsSpan(0, length))}\nexpected {Hex(expected)}");
    }

    [Test]
    public void WriteBindingSuccess_RejectsIPv4MappedIPv6()
    {
        var buffer = new byte[StunBindingCodec.MaxResponseLength];
        var mapped = IPAddress.Parse("::ffff:192.0.2.1");

        var ex = Assert.Throws<ArgumentException>(() =>
            StunBindingCodec.WriteBindingSuccess(buffer, TransactionId, mapped, 1));

        Assert.That(ex!.ParamName, Is.EqualTo("address"));
    }

    [Test]
    public void WriteBindingSuccess_RejectsShortBuffer()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            StunBindingCodec.WriteBindingSuccess(new byte[31], TransactionId, IPAddress.Loopback, 1));

        Assert.That(ex!.ParamName, Is.EqualTo("dest"));
    }

    [Test]
    public void WriteBindingSuccess_RejectsWrongTransactionIdLength()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            StunBindingCodec.WriteBindingSuccess(new byte[44], new byte[11], IPAddress.Loopback, 1));

        Assert.That(ex!.ParamName, Is.EqualTo("transactionId"));
    }

    // --- round trip through the test-side decoder ---

    [TestCase("127.0.0.1", 0)]
    [TestCase("127.0.0.1", 65535)]
    [TestCase("203.0.113.9", 3478)]
    [TestCase("::1", 0)]
    [TestCase("::1", 65535)]
    [TestCase("2001:db8::1", 49152)]
    public void RoundTrip(string address, int port)
    {
        var random = new Random(port + address.Length);
        var tid = new byte[12];
        random.NextBytes(tid);
        var buffer = new byte[StunBindingCodec.MaxResponseLength];
        var expected = new IPEndPoint(IPAddress.Parse(address), port);

        var length = StunBindingCodec.WriteBindingSuccess(buffer, tid, expected.Address, expected.Port);
        var ok = StunTestMessages.TryReadXorMappedAddress(buffer.AsSpan(0, length), out var decoded);

        Assert.That(ok, Is.True, $"could not decode {Hex(buffer.AsSpan(0, length))}");
        Assert.That(decoded, Is.EqualTo(expected), $"decoded {decoded} from {Hex(buffer.AsSpan(0, length))}");
        Assert.That(buffer[8..20], Is.EqualTo(tid), "transaction id must be echoed");
    }
}
