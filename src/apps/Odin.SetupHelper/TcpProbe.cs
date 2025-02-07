using System.Net;
using System.Net.Sockets;
using System.Text;
using Odin.Core;
using Odin.Core.Cache;
using Odin.Core.Util;

namespace Odin.SetupHelper;

public class TcpProbe(IGenericMemoryCache cache)
{
    public record TcpProbeResult(bool Success, string Message);
    public async Task<TcpProbeResult> ProbeAsync(string ipOrDomain, string hostPort)
    {
        ipOrDomain = ipOrDomain.ToLower();

        if (!IPAddress.TryParse(ipOrDomain, out _))
        {
            if (!AsciiDomainNameValidator.TryValidateDomain(ipOrDomain))
            {
                return new TcpProbeResult(false, "Invalid domain name");
            }
        }

        if (!int.TryParse(hostPort, out var port))
        {
            return new TcpProbeResult(false, "Invalid port number");
        }

        if (port is < 1 or > 65535)
        {
            return new TcpProbeResult(false, "Port number out of range");
        }

        var cacheKey = $"tcp:{ipOrDomain}:{port}";
        if (cache.TryGet<TcpProbeResult>(cacheKey, out var result) && result != null)
        {
            return result with { Message = $"{result.Message} [cache hit]" };
        }

        try
        {
            using var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await tcpClient.ConnectAsync(ipOrDomain, port, cts.Token);
            await using var networkStream = tcpClient.GetStream();

            const string message = "hello from the other side";
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(message), cts.Token);
            
            var buffer = new byte[256];
            var bytesRead = await networkStream.ReadAsync(buffer, cts.Token);
            var receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Reverse();

            if (message == receivedMessage)
            {
                result = new TcpProbeResult(true, $"Successfully connected to {ipOrDomain}:{port}");
            }
            else
            {
                result = new TcpProbeResult(false, $"Successfully connected to {ipOrDomain}:{port}, but did not get the expected response");    
            }
            
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }
        catch (Exception ex)
        {
            result = new TcpProbeResult(false, $"Failed to connect to {ipOrDomain}:{port}: {ex.Message}");
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }
    }
}
