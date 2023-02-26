using System;
using System.Net.Http;
using Refit;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Base;

/// <summary>
/// Client used to communicate with this instance of the identity server for system purposes
/// </summary>
public static class SystemHttpClient
{
    public static T CreateHttps<T>(OdinId dotYouId)
    {
        //TODO: add a certificate for the stoker
        // var handler = new HttpClientHandler();
        // handler.ClientCertificates.Add(cert);
        // handler.AllowAutoRedirect = false;
        //handler.ServerCertificateCustomValidationCallback
        //handler.SslProtocols = SslProtocols.None;// | SslProtocols.Tls13;

        var client = new HttpClient();
        client.BaseAddress = new UriBuilder() { Scheme = "https", Host = dotYouId }.Uri;

        var token = Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e"); //TODO: read from config
        client.DefaultRequestHeaders.Add("SY4829", token.ToString());

        return RestService.For<T>(client);
    }
    
    public static T CreateHttp<T>(OdinId dotYouId)
    {
        var handler = new HttpClientHandler();
        handler.AllowAutoRedirect = false;
        var client = new HttpClient(handler);
        
        client.BaseAddress = new UriBuilder() { Scheme = "http", Host = dotYouId }.Uri;
        //TODO: need to handle the fact this is over http 
        var token = Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e"); //TODO: read from config
        client.DefaultRequestHeaders.Add("SY4829", token.ToString());

        return RestService.For<T>(client);
    }
}