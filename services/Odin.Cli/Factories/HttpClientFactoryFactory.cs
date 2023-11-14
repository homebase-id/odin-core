using System.Security.Authentication;

namespace Odin.Cli.Factories;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

public interface ICliHttpClientFactory
{
    HttpClient Create(string hostAndPort, string apiKeyHeader, string apiKey);
}

//

public class CliHttpClientFactory : ICliHttpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public CliHttpClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _httpClientFactory.Register(nameof(CliHttpClientFactory), builder => builder.ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                SslProtocols = SslProtocols.None, //allow OS to choose;
            };
            return handler;
        }));
    }

    //

    public HttpClient Create(string hostAndPort, string apiKeyHeader, string apiKey)
    {

        var httpClient = _httpClientFactory.CreateClient(nameof(CliHttpClientFactory));
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

