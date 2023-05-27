using System;
using System.Net.Http;
using HttpClientFactoryLite;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using Youverse.Core.Identity;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Youverse.Core.Services.Base;

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
    
    public SystemHttpClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
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

        var token = Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e"); //TODO: read from config
        client.DefaultRequestHeaders.Add("SY4829", token.ToString());

        return RestService.For<T>(client);
    }
    
    public T CreateHttp<T>(OdinId odinId)
    {
        var client = _httpClientFactory.CreateClient<SystemHttpClient>();
        
        client.BaseAddress = new UriBuilder() { Scheme = "http", Host = odinId }.Uri;
        //TODO: need to handle the fact this is over http 
        var token = Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e"); //TODO: read from config
        client.DefaultRequestHeaders.Add("SY4829", token.ToString());

        return RestService.For<T>(client);
    }
}


