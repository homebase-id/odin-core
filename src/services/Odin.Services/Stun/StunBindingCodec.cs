#nullable enable
using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Odin.Services.Stun;

public enum StunParseStatus
{
    /// <summary>A well-formed STUN Binding request; answer it.</summary>
    BindingRequest,

    /// <summary>Too short, wrong magic cookie, or top two bits set. Not STUN at all; drop.</summary>
    NotStun,

    /// <summary>STUN, but an indication, a response, or a non-Binding method (e.g. TURN Allocate); drop.</summary>
    NotBindingRequest,

    /// <summary>The header's message length disagrees with the datagram or is not 4-aligned; drop.</summary>
    BadLength,

    /// <summary>Longer than <see cref="StunBindingCodec.MaxDatagramLength"/>; drop unread.</summary>
    Oversized,
}

/// <summary>
/// The RFC 8489 subset a STUN Binding responder needs: recognise a Binding request and write a
/// Binding success response carrying one XOR-MAPPED-ADDRESS. Pure byte work, no I/O.
///
/// Attributes on the request are NOT walked. That is a deliberate deviation from §6.3.1 (a 420
/// for unknown comprehension-required attributes): the srflx-gathering request an ICE agent sends
/// carries none, and an error-response encoder would be one more thing to reflect. The response
/// carries no SOFTWARE and no FINGERPRINT, so it is a fixed 32 bytes (IPv4) or 44 bytes (IPv6).
/// </summary>
public static class StunBindingCodec
{
    public const int HeaderLength = 20;

    /// <summary>
    /// Largest datagram we read. RFC 8489 §6.1 asks senders to stay under the path MTU, and 1280
    /// is the IPv6 minimum; the Binding request ICE sends for gathering is 20 bytes.
    /// </summary>
    public const int MaxDatagramLength = 1280;

    /// <summary>Header + attribute header + family/port + IPv6 address.</summary>
    public const int MaxResponseLength = HeaderLength + 4 + 4 + 16;

    public const uint MagicCookie = 0x2112A442;
    public const ushort BindingRequestType = 0x0001;
    public const ushort BindingSuccessType = 0x0101;
    public const ushort XorMappedAddressAttribute = 0x0020;

    private const byte FamilyIPv4 = 0x01;
    private const byte FamilyIPv6 = 0x02;

    public static StunParseStatus TryParseBindingRequest(ReadOnlySpan<byte> datagram, out ReadOnlySpan<byte> transactionId)
    {
        transactionId = default;

        if (datagram.Length > MaxDatagramLength)
        {
            return StunParseStatus.Oversized;
        }

        if (datagram.Length < HeaderLength)
        {
            return StunParseStatus.NotStun;
        }

        // §5: the two most significant bits of every STUN message are zero.
        if ((datagram[0] & 0xC0) != 0)
        {
            return StunParseStatus.NotStun;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(datagram[4..8]) != MagicCookie)
        {
            return StunParseStatus.NotStun;
        }

        var messageLength = BinaryPrimitives.ReadUInt16BigEndian(datagram[2..4]);
        if (messageLength % 4 != 0 || messageLength != datagram.Length - HeaderLength)
        {
            return StunParseStatus.BadLength;
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(datagram[..2]) != BindingRequestType)
        {
            return StunParseStatus.NotBindingRequest;
        }

        transactionId = datagram[8..HeaderLength];
        return StunParseStatus.BindingRequest;
    }

    /// <summary>
    /// Writes a Binding success response for <paramref name="transactionId"/> whose only attribute
    /// is XOR-MAPPED-ADDRESS for <paramref name="address"/>:<paramref name="port"/>. Returns the
    /// number of bytes written (32 or 44). The address must be the family the request arrived on;
    /// an IPv4-mapped IPv6 address (what a dual-mode socket reports for IPv4 peers) is rejected so
    /// the caller cannot forget to unmap it.
    /// </summary>
    public static int WriteBindingSuccess(Span<byte> dest, ReadOnlySpan<byte> transactionId, IPAddress address, int port)
    {
        if (transactionId.Length != 12)
        {
            throw new ArgumentException("A STUN transaction id is 12 bytes", nameof(transactionId));
        }
        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Not a UDP port");
        }
        if (address.IsIPv4MappedToIPv6)
        {
            throw new ArgumentException(
                "Unmap IPv4-mapped IPv6 addresses before encoding; the reply must carry the family the request arrived on",
                nameof(address));
        }

        var (family, addressLength) = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => (FamilyIPv4, 4),
            AddressFamily.InterNetworkV6 => (FamilyIPv6, 16),
            _ => throw new ArgumentException($"Unsupported address family {address.AddressFamily}", nameof(address)),
        };

        var attributeLength = 4 + addressLength;
        var total = HeaderLength + 4 + attributeLength;
        if (dest.Length < total)
        {
            throw new ArgumentException($"Need {total} bytes, got {dest.Length}", nameof(dest));
        }

        BinaryPrimitives.WriteUInt16BigEndian(dest[..2], BindingSuccessType);
        BinaryPrimitives.WriteUInt16BigEndian(dest[2..4], (ushort)(4 + attributeLength));
        BinaryPrimitives.WriteUInt32BigEndian(dest[4..8], MagicCookie);
        transactionId.CopyTo(dest[8..HeaderLength]);

        var attr = dest[HeaderLength..total];
        BinaryPrimitives.WriteUInt16BigEndian(attr[..2], XorMappedAddressAttribute);
        BinaryPrimitives.WriteUInt16BigEndian(attr[2..4], (ushort)attributeLength);
        attr[4] = 0;
        attr[5] = family;

        // §14.2: port XOR the 16 most significant bits of the cookie; address XOR the cookie
        // (IPv4) or the cookie followed by the transaction id (IPv6), i.e. header bytes 4..20.
        BinaryPrimitives.WriteUInt16BigEndian(attr[6..8], (ushort)(port ^ (MagicCookie >> 16)));
        if (!address.TryWriteBytes(attr[8..], out var written) || written != addressLength)
        {
            throw new InvalidOperationException($"IPAddress.TryWriteBytes wrote {written} bytes, expected {addressLength}");
        }
        var xorKey = dest[4..HeaderLength];
        for (var i = 0; i < addressLength; i++)
        {
            attr[8 + i] ^= xorKey[i];
        }

        return total;
    }

    /// <summary>
    /// Decodes the XOR-MAPPED-ADDRESS of a Binding success response written by
    /// <see cref="WriteBindingSuccess"/> or any RFC 8489 responder. Walks attributes so a response
    /// that also carries SOFTWARE or FINGERPRINT decodes too. Used by tests and tooling.
    /// </summary>
    public static bool TryReadXorMappedAddress(ReadOnlySpan<byte> response, out IPEndPoint? endpoint)
    {
        endpoint = null;

        if (response.Length < HeaderLength ||
            BinaryPrimitives.ReadUInt16BigEndian(response[..2]) != BindingSuccessType ||
            BinaryPrimitives.ReadUInt32BigEndian(response[4..8]) != MagicCookie)
        {
            return false;
        }

        var messageLength = BinaryPrimitives.ReadUInt16BigEndian(response[2..4]);
        if (messageLength % 4 != 0 || messageLength != response.Length - HeaderLength)
        {
            return false;
        }

        var xorKey = response[4..HeaderLength];
        var attributes = response[HeaderLength..];
        while (attributes.Length >= 4)
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(attributes[..2]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(attributes[2..4]);
            if (attributes.Length < 4 + length)
            {
                return false;
            }

            if (type == XorMappedAddressAttribute)
            {
                var value = attributes[4..(4 + length)];
                var addressLength = value.Length >= 2
                    ? value[1] switch { FamilyIPv4 => 4, FamilyIPv6 => 16, _ => -1 }
                    : -1;
                if (addressLength < 0 || value.Length != 4 + addressLength)
                {
                    return false;
                }

                var port = BinaryPrimitives.ReadUInt16BigEndian(value[2..4]) ^ (int)(MagicCookie >> 16);
                Span<byte> addressBytes = stackalloc byte[addressLength];
                for (var i = 0; i < addressLength; i++)
                {
                    addressBytes[i] = (byte)(value[4 + i] ^ xorKey[i]);
                }
                endpoint = new IPEndPoint(new IPAddress(addressBytes), port);
                return true;
            }

            // Attribute values are padded to a 4-byte boundary (§14).
            var padded = (length + 3) & ~3;
            if (attributes.Length < 4 + padded)
            {
                return false;
            }
            attributes = attributes[(4 + padded)..];
        }

        return false;
    }
}
