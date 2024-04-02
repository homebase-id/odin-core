using System;
using System.Net.Http;
using HttpClientFactoryLite;

namespace Odin.Core.Util;

public static class HttpClientLiteFactoryExtensions
{
    public static HttpClient CreateClient<T>(this IHttpClientFactory httpClientFactory, Uri baseAddress)
    {
        var httpClient = httpClientFactory.CreateClient<T>();
        httpClient.BaseAddress = baseAddress;
        return httpClient;
    }

}