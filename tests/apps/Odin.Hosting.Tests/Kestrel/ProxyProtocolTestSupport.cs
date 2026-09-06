#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests.Kestrel;

/// <summary>
/// Builds PROXY protocol headers and clients that write one before TLS, the way an L4 load
/// balancer does. The HttpClient variant connects to loopback itself (so the request URL only
/// supplies SNI and the Host header) and prepends the header on every new connection.
/// </summary>
internal static class ProxyProtocolTestSupport
{
    public static byte[] V2Header(IPAddress source, int sourcePort, IPAddress destination, int destinationPort)
    {
        var v4 = source.AddressFamily == AddressFamily.InterNetwork;
        var addressLength = v4 ? 12 : 36;
        var header = new byte[16 + addressLength];
        "\r\n\r\n\0\r\nQUIT\n"u8.CopyTo(header);
        header[12] = 0x21; // v2, PROXY
        header[13] = (byte)((v4 ? 0x10 : 0x20) | 0x01); // INET / INET6, STREAM
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(14, 2), (ushort)addressLength);
        var ipLength = v4 ? 4 : 16;
        source.GetAddressBytes().CopyTo(header, 16);
        destination.GetAddressBytes().CopyTo(header, 16 + ipLength);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(16 + ipLength * 2, 2), (ushort)sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(16 + ipLength * 2 + 2, 2), (ushort)destinationPort);
        return header;
    }

    public static byte[] V2LocalHeader()
    {
        var header = new byte[16];
        "\r\n\r\n\0\r\nQUIT\n"u8.CopyTo(header);
        header[12] = 0x20; // v2, LOCAL
        header[13] = 0x00; // UNSPEC
        return header;
    }

    public static byte[] V1Header(IPAddress source, int sourcePort, IPAddress destination, int destinationPort)
    {
        var family = source.AddressFamily == AddressFamily.InterNetwork ? "TCP4" : "TCP6";
        return Encoding.ASCII.GetBytes($"PROXY {family} {source} {destination} {sourcePort} {destinationPort}\r\n");
    }

    public static async Task<Stream> ConnectAsync(int port, byte[]? proxyHeader, CancellationToken ct = default)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(IPAddress.Loopback, port, ct);
        var stream = new NetworkStream(socket, ownsSocket: true);
        if (proxyHeader != null)
        {
            await stream.WriteAsync(proxyHeader, ct);
        }
        return stream;
    }

    public static HttpClient CreateHttpClient(int port, byte[]? proxyHeader)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (_, ct) => new ValueTask<Stream>(ConnectAsync(port, proxyHeader, ct)),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>TLS handshake through the proxy path; returns the certificate the server presented for <paramref name="sni"/>.</summary>
    public static async Task<X509Certificate2?> HandshakeAsync(int port, byte[]? proxyHeader, string sni)
    {
        await using var transport = await ConnectAsync(port, proxyHeader);
        X509Certificate2? presented = null;
        await using var ssl = new SslStream(transport, false, (_, cert, _, _) =>
        {
            presented = cert == null ? null : new X509Certificate2(cert);
            return true;
        });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = sni });
        return presented;
    }

    public static IEnumerable<KeyValuePair<string, string>> ListenEntryEnv(
        int index, int httpPort, int httpsPort, params string[] trustedProxies)
    {
        var prefix = $"Host__IPAddressListenList__{index}__";
        yield return new(prefix + "Ip", "*");
        yield return new(prefix + "HttpPort", httpPort.ToString());
        yield return new(prefix + "HttpsPort", httpsPort.ToString());
        yield return new(prefix + "ProxyProtocol__Enabled", "true");
        for (var i = 0; i < trustedProxies.Length; i++)
        {
            yield return new(prefix + $"ProxyProtocol__TrustedProxies__{i}", trustedProxies[i]);
        }
    }

    public static void ClearEnv(IEnumerable<KeyValuePair<string, string>> vars)
    {
        foreach (var (key, _) in vars)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}
