using Odin.Core.Cache;
using Odin.Core.Http;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Util;

namespace Odin.SetupHelper;

public class HttpProbe(IDynamicHttpClientFactory httpClientFactory, IGenericMemoryCache cache)
{
    public record HttpProbeResult(bool Success, string Message);
    public async Task<HttpProbeResult> ProbeAsync(
        string scheme,
        string domainName,
        string hostPort,
        Guid? correlationId = null)
    {
        scheme = scheme.ToLower();
        if (scheme != "http" && scheme != "https")
        {
            return new HttpProbeResult(false, "Invalid scheme");
        }
        
        domainName = domainName.ToLower();
        if (!AsciiDomainNameValidator.TryValidateDomain(domainName))
        {
            return new HttpProbeResult(false, "Invalid domain name");
        }

        if (!int.TryParse(hostPort, out var port))
        {
            return new HttpProbeResult(false, "Invalid port number");
        }

        if (port is < 1 or > 65535)
        {
            return new HttpProbeResult(false, "Port number out of range");
        }

        var uri = new Uri($"{scheme}://{domainName}:{port}/.well-known/acme-challenge/ping");
        var cacheKey = uri.ToString();
        if (cache.TryGet<HttpProbeResult>(cacheKey, out var result) && result != null)
        {
            return result with { Message = $"{result.Message} [cache hit]" };
        }

        var client = httpClientFactory.CreateClient($"{domainName}:{port}");
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (correlationId.HasValue)
            {
                request.Headers.Add(ICorrelationContext.DefaultHeaderName, correlationId.ToString());
            }
            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                if (body == "pong")
                {
                    
                    result = new HttpProbeResult(true, $"Successfully probed {scheme}://{domainName}:{port}");
                    cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
                    return result;

                }
                result = new HttpProbeResult(false, $"Successfully probed {scheme}://{domainName}:{port}, but received unexpected response");
                cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
                return result;
            }
            result = new HttpProbeResult(false, $"Failed to probe {scheme}://{domainName}:{port}, {domainName} says: {response.ReasonPhrase}");
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }
        catch (HttpRequestException e)
        {
            result = new HttpProbeResult(false, $"Failed to probe {scheme}://{domainName}:{port}: {e.Message}");
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }
        catch (TaskCanceledException)
        {
            result = new HttpProbeResult(false, $"Failed to probe {scheme}://{domainName}:{port}: time out");
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }
        catch (Exception)
        {
            result = new HttpProbeResult(false, $"Failed to probe {scheme}://{domainName}:{port}: unknown server error");
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }
        
    }
}