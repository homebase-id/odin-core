using System;
using System.Net.Http;
using HttpClientFactoryLite;
using Odin.Core.Identity;
using Odin.Core.Services.Configuration;
using Refit;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Core.Services.Base;

/// <summary>
/// Client used to communicate with this instance of the identity server for system purposes
/// </summary>

public interface ISystemHttpClient
{
    T CreateHttps<T>(OdinId odinId);
    T CreateHttp<T>(OdinId odinId);
}

public class SystemHttpClient : ISystemHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OdinConfiguration _config;

    public const string HeaderName = "SY4829";
    
    public SystemHttpClient(IHttpClientFactory httpClientFactory, OdinConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _httpClientFactory.Register<SystemHttpClient>(builder => builder
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false, 
                UseCookies = false // DO NOT CHANGE!
            }));
    }
        
    public T CreateHttps<T>(OdinId odinId)
    {
        //TODO: add a certificate for the stoker
        // var handler = new HttpClientHandler();
        // handler.ClientCertificates.Add(cert);
        // handler.AllowAutoRedirect = false;
        //handler.ServerCertificateCustomValidationCallback
        //handler.SslProtocols = SslProtocols.None;// | SslProtocols.Tls13;

        var client = _httpClientFactory.CreateClient<SystemHttpClient>();
        client.BaseAddress = new UriBuilder() { Scheme = "https", Host = odinId }.Uri;
        
        var token = _config.Host.SystemProcessApiKey; 
        client.DefaultRequestHeaders.Add(HeaderName, token.ToString());

        return RestService.For<T>(client);
    }
    
    public T CreateHttp<T>(OdinId odinId)
    {
        var client = _httpClientFactory.CreateClient<SystemHttpClient>();
        
        client.BaseAddress = new UriBuilder() { Scheme = "http", Host = odinId }.Uri;
        //TODO: need to handle the fact this is over http 

        var token = _config.Host.SystemProcessApiKey; 
        client.DefaultRequestHeaders.Add(HeaderName, token.ToString());

        return RestService.For<T>(client);
    }
}


