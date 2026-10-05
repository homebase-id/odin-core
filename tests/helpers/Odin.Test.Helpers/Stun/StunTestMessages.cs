#nullable enable
using System;
using System.Buffers.Binary;
using System.Net;
using Odin.Services.Stun;

namespace Odin.Test.Helpers.Stun;

/// <summary>
/// The client side of a STUN Binding exchange, for tests: build the bare request ICE sends when
/// gathering, and decode the fixed-layout reply <see cref="StunBindingCodec"/> writes.
/// </summary>
public static class StunTestMessages
{
    /// <summary>A 20-byte Binding request with no attributes.</summary>
    public static byte[] BareBindingRequest(ReadOnlySpan<byte> transactionId)
    {
        if (transactionId.Length != 12)
        {
            throw new ArgumentException("A STUN transaction id is 12 bytes", nameof(transactionId));
        }

        var request = new byte[StunBindingCodec.HeaderLength];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), StunBindingCodec.BindingRequestType);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4, 4), StunBindingCodec.MagicCookie);
        transactionId.CopyTo(request.AsSpan(8));
        return request;
    }

    /// <summary>
    /// Decodes the reply our responder sends: a Binding success whose only attribute is
    /// XOR-MAPPED-ADDRESS (32 bytes for IPv4, 44 for IPv6). Anything else returns false.
    /// </summary>
    public static bool TryReadXorMappedAddress(ReadOnlySpan<byte> response, out IPEndPoint? endpoint)
    {
        endpoint = null;

        if (response.Length is not (32 or 44) ||
            BinaryPrimitives.ReadUInt16BigEndian(response[..2]) != StunBindingCodec.BindingSuccessType ||
            BinaryPrimitives.ReadUInt32BigEndian(response[4..8]) != StunBindingCodec.MagicCookie ||
            BinaryPrimitives.ReadUInt16BigEndian(response[2..4]) != response.Length - StunBindingCodec.HeaderLength ||
            BinaryPrimitives.ReadUInt16BigEndian(response[20..22]) != StunBindingCodec.XorMappedAddressAttribute ||
            BinaryPrimitives.ReadUInt16BigEndian(response[22..24]) != response.Length - 24)
        {
            return false;
        }

        var addressLength = response[25] switch
        {
            StunBindingCodec.FamilyIPv4 when response.Length == 32 => 4,
            StunBindingCodec.FamilyIPv6 when response.Length == 44 => 16,
            _ => 0,
        };
        if (addressLength == 0)
        {
            return false;
        }

        // XOR key is header bytes 4..20: the magic cookie followed by the transaction id.
        var xorKey = response[4..StunBindingCodec.HeaderLength];
        var port = BinaryPrimitives.ReadUInt16BigEndian(response[26..28]) ^ (int)(StunBindingCodec.MagicCookie >> 16);
        Span<byte> addressBytes = stackalloc byte[addressLength];
        for (var i = 0; i < addressLength; i++)
        {
            addressBytes[i] = (byte)(response[28 + i] ^ xorKey[i]);
        }
        endpoint = new IPEndPoint(new IPAddress(addressBytes), port);
        return true;
    }
}
