using System;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Services.Configuration;
using Refit;

namespace Odin.Services.Base;

/// <summary>
/// Client used to communicate with this instance of the identity server for system purposes
/// </summary>

public interface ISystemHttpClient
{
    T CreateHttps<T>(OdinId odinId);
}

public class SystemHttpClient : ISystemHttpClient
{
    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly OdinConfiguration _config;

    public const string HeaderName = "SY4829";
    
    public SystemHttpClient(IDynamicHttpClientFactory httpClientFactory, OdinConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }
        
    public T CreateHttps<T>(OdinId odinId)
    {
        var client = _httpClientFactory.CreateClient(odinId);
        client.BaseAddress = new UriBuilder
        {
            Scheme = "https",
            Host = odinId,
            Port = _config.Host.DefaultHttpsPort
        }.Uri;
        
        var token = _config.Host.SystemProcessApiKey; 
        client.DefaultRequestHeaders.Add(HeaderName, token.ToString());

        return RestService.For<T>(client);
    }

}


