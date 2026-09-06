#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Odin.Hosting.Kestrel;

public enum ProxyProtocolParseStatus
{
    Success,
    NeedMoreData,
    Invalid,
}

/// <summary>
/// A parsed PROXY protocol header. <see cref="Source"/> / <see cref="Destination"/> are null for
/// LOCAL (v2) and UNKNOWN (v1) headers, where the proxy is talking on its own behalf (health checks)
/// and the transport endpoint must be kept.
/// </summary>
public sealed record ProxyProtocolHeader(int Version, IPEndPoint? Source, IPEndPoint? Destination)
{
    public bool HasAddresses => Source != null && Destination != null;
}

/// <summary>
/// Parses PROXY protocol v1 (text) and v2 (binary) headers as sent by HAProxy / OpenStack Octavia
/// (<c>PROXY</c> / <c>PROXYV2</c> pool protocols). Only the leading header is consumed; whatever
/// follows (normally the TLS ClientHello) is left in the buffer.
/// </summary>
public static class ProxyProtocolParser
{
    public static ReadOnlySpan<byte> V2Signature => "\r\n\r\n\0\r\nQUIT\n"u8;
    private static ReadOnlySpan<byte> V1Prefix => "PROXY "u8;

    private const int V2HeaderLength = 16;
    private const int V1MaxLength = 107;

    public static ProxyProtocolParseStatus TryParse(
        in ReadOnlySequence<byte> buffer,
        out ProxyProtocolHeader? header,
        out SequencePosition consumed)
    {
        header = null;
        consumed = buffer.Start;

        // Enough bytes to tell the two formats apart (and to read the v2 fixed header)?
        var probeLength = (int)Math.Min(buffer.Length, V2HeaderLength);
        Span<byte> probe = stackalloc byte[V2HeaderLength];
        buffer.Slice(0, probeLength).CopyTo(probe);
        probe = probe[..probeLength];

        if (StartsWithOrIsPrefixOf(probe, V2Signature, out var isV2Prefix))
        {
            if (probe.Length < V2HeaderLength)
            {
                return ProxyProtocolParseStatus.NeedMoreData;
            }

            return TryParseV2(buffer, probe, out header, out consumed);
        }

        if (isV2Prefix)
        {
            return ProxyProtocolParseStatus.NeedMoreData;
        }

        if (StartsWithOrIsPrefixOf(probe, V1Prefix, out var isV1Prefix))
        {
            return TryParseV1(buffer, out header, out consumed);
        }

        return isV1Prefix ? ProxyProtocolParseStatus.NeedMoreData : ProxyProtocolParseStatus.Invalid;
    }

    // true when `data` starts with `pattern`; `isPrefix` is true when `data` is shorter than
    // `pattern` but matches as far as it goes (so more bytes could still complete it).
    private static bool StartsWithOrIsPrefixOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern, out bool isPrefix)
    {
        isPrefix = false;
        if (data.Length >= pattern.Length)
        {
            return data[..pattern.Length].SequenceEqual(pattern);
        }

        isPrefix = pattern[..data.Length].SequenceEqual(data);
        return false;
    }

    private static ProxyProtocolParseStatus TryParseV2(
        in ReadOnlySequence<byte> buffer,
        ReadOnlySpan<byte> fixedHeader,
        out ProxyProtocolHeader? header,
        out SequencePosition consumed)
    {
        header = null;
        consumed = buffer.Start;

        var versionAndCommand = fixedHeader[12];
        var version = versionAndCommand >> 4;
        var command = versionAndCommand & 0x0F;
        if (version != 2 || command > 1)
        {
            return ProxyProtocolParseStatus.Invalid;
        }

        var familyAndTransport = fixedHeader[13];
        var family = familyAndTransport >> 4;
        var addressLength = BinaryPrimitives.ReadUInt16BigEndian(fixedHeader.Slice(14, 2));
        var totalLength = V2HeaderLength + addressLength;
        if (buffer.Length < totalLength)
        {
            return ProxyProtocolParseStatus.NeedMoreData;
        }

        consumed = buffer.GetPosition(totalLength);

        // LOCAL: the proxy itself is connecting (health checks). UNSPEC: no address to apply.
        if (command == 0 || family == 0)
        {
            header = new ProxyProtocolHeader(2, null, null);
            return ProxyProtocolParseStatus.Success;
        }

        var addressBytesNeeded = family switch
        {
            1 => 12, // INET:  4 + 4 + 2 + 2
            2 => 36, // INET6: 16 + 16 + 2 + 2
            _ => -1, // UNIX or unknown: not meaningful on a TCP listener
        };
        if (addressBytesNeeded < 0 || addressLength < addressBytesNeeded)
        {
            return ProxyProtocolParseStatus.Invalid;
        }

        Span<byte> addresses = stackalloc byte[addressBytesNeeded];
        buffer.Slice(V2HeaderLength, addressBytesNeeded).CopyTo(addresses);

        var ipLength = family == 1 ? 4 : 16;
        var source = new IPAddress(addresses[..ipLength]);
        var destination = new IPAddress(addresses.Slice(ipLength, ipLength));
        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(addresses.Slice(ipLength * 2, 2));
        var destinationPort = BinaryPrimitives.ReadUInt16BigEndian(addresses.Slice(ipLength * 2 + 2, 2));

        header = new ProxyProtocolHeader(2, new IPEndPoint(source, sourcePort), new IPEndPoint(destination, destinationPort));
        return ProxyProtocolParseStatus.Success;
    }

    private static ProxyProtocolParseStatus TryParseV1(
        in ReadOnlySequence<byte> buffer,
        out ProxyProtocolHeader? header,
        out SequencePosition consumed)
    {
        header = null;
        consumed = buffer.Start;

        var searchLength = (int)Math.Min(buffer.Length, V1MaxLength);
        Span<byte> line = stackalloc byte[V1MaxLength];
        buffer.Slice(0, searchLength).CopyTo(line);
        line = line[..searchLength];

        var lf = line.IndexOf((byte)'\n');
        if (lf < 0)
        {
            return searchLength >= V1MaxLength ? ProxyProtocolParseStatus.Invalid : ProxyProtocolParseStatus.NeedMoreData;
        }

        if (lf == 0 || line[lf - 1] != (byte)'\r')
        {
            return ProxyProtocolParseStatus.Invalid;
        }

        var text = Encoding.ASCII.GetString(line[..(lf - 1)]);
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "PROXY")
        {
            return ProxyProtocolParseStatus.Invalid;
        }

        consumed = buffer.GetPosition(lf + 1);

        if (parts[1] == "UNKNOWN")
        {
            header = new ProxyProtocolHeader(1, null, null);
            return ProxyProtocolParseStatus.Success;
        }

        if (parts.Length != 6 || (parts[1] != "TCP4" && parts[1] != "TCP6"))
        {
            return ProxyProtocolParseStatus.Invalid;
        }

        if (!IPAddress.TryParse(parts[2], out var source) ||
            !IPAddress.TryParse(parts[3], out var destination) ||
            !ushort.TryParse(parts[4], out var sourcePort) ||
            !ushort.TryParse(parts[5], out var destinationPort))
        {
            return ProxyProtocolParseStatus.Invalid;
        }

        header = new ProxyProtocolHeader(1, new IPEndPoint(source, sourcePort), new IPEndPoint(destination, destinationPort));
        return ProxyProtocolParseStatus.Success;
    }
}
