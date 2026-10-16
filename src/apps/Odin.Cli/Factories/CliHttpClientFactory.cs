using Microsoft.Extensions.Logging.Abstractions;
using Odin.Core.Http;

namespace Odin.Cli.Factories;

//

public static class CliHttpClientFactory
{
    private static readonly IDynamicHttpClientFactory HttpClientFactory;

    static CliHttpClientFactory()
    {
        HttpClientFactory = new DynamicHttpClientFactory(NullLogger<DynamicHttpClientFactory>.Instance);
    }

    //

    public static HttpClient Create(string hostAndPort, string apiKeyHeader, string apiKey, string basePath = "api/admin/v1")
    {
        var httpClient = HttpClientFactory.CreateClient(hostAndPort);
        httpClient.BaseAddress = CreateUriFromHostname(hostAndPort, basePath);
        httpClient.DefaultRequestHeaders.Add(apiKeyHeader, apiKey);
        return httpClient;
    }

    //

    private static Uri CreateUriFromHostname(string hostAndPort, string basePath, string scheme = "https")
    {
        if (string.IsNullOrWhiteSpace(hostAndPort))
        {
            throw new ArgumentException("Hostname cannot be null or whitespace.", nameof(hostAndPort));
        }

        hostAndPort = $"{scheme}://{hostAndPort}/{basePath}/";

        return new Uri(hostAndPort);
    }

}

