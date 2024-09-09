using System.Net.Sockets;
using Odin.Core.Cache;
using Odin.Core.Util;

namespace Odin.SetupHelper;

public class TcpProbe(IGenericMemoryCache cache)
{
    public record TcpProbeResult(bool Success, string Message);
    public async Task<TcpProbeResult> ProbeAsync(string domainName, string hostPort)
    {
        domainName = domainName.ToLower();
        if (!AsciiDomainNameValidator.TryValidateDomain(domainName))
        {
            return new TcpProbeResult(false, "Invalid domain name");
        }

        if (!int.TryParse(hostPort, out var port))
        {
            return new TcpProbeResult(false, "Invalid port number");
        }

        if (port is < 1 or > 65535)
        {
            return new TcpProbeResult(false, "Port number out of range");
        }

        var cacheKey = $"tcp:{domainName}:{port}";
        if (cache.TryGet<TcpProbeResult>(cacheKey, out var result) && result != null)
        {
            return result with { Message = $"{result.Message} [cache hit]" };
        }

        try
        {
            using var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await tcpClient.ConnectAsync(domainName, port, cts.Token);
            result = new TcpProbeResult(true, $"Successfully connected to TCP: {domainName}:{port}");
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
            return result;
        }
        catch (Exception)
        {
            result = new TcpProbeResult(false, $"Failed to connect to TCP: {domainName}:{port}");
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
            return result;
        }
    }
}
