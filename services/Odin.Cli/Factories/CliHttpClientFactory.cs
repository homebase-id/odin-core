using System.Security.Authentication;

namespace Odin.Cli.Factories;
using HttpClientFactory = HttpClientFactoryLite.HttpClientFactory;

// public interface ICliHttpClientFactory
// {
//     HttpClient Create(string hostAndPort, string apiKeyHeader, string apiKey);
// }

//

public static class CliHttpClientFactory
{
    private static readonly HttpClientFactory HttpClientFactory;

    static CliHttpClientFactory()
    {
        HttpClientFactory = new HttpClientFactory();
        HttpClientFactory.Register(nameof(CliHttpClientFactory), builder => builder.ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
            };
            return handler;
        }));
    }

    //

    public static HttpClient Create(string hostAndPort, string apiKeyHeader, string apiKey)
    {

        var httpClient = HttpClientFactory.CreateClient(nameof(CliHttpClientFactory));
        httpClient.BaseAddress = CreateUriFromHostname(hostAndPort);
        httpClient.DefaultRequestHeaders.Add(apiKeyHeader, apiKey);
        return httpClient;
    }

    //

    private static Uri CreateUriFromHostname(string hostAndPort, string scheme = "https")
    {
        if (string.IsNullOrWhiteSpace(hostAndPort))
        {
            throw new ArgumentException("Hostname cannot be null or whitespace.", nameof(hostAndPort));
        }

        hostAndPort = $"{scheme}://{hostAndPort}/api/admin/v1/";

        return new Uri(hostAndPort);
    }

}

