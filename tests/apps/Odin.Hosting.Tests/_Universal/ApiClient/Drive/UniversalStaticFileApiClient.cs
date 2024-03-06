using System;
using System.Net.Http;
using System.Net.Mime;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive;

public class UniversalStaticFileApiClient
{
    private readonly OdinId _identity;
    private readonly IApiClientFactory _factory;

    public UniversalStaticFileApiClient(OdinId identity, IApiClientFactory factory)
    {
        _identity = identity;
        _factory = factory;
    }

    public async Task<ApiResponse<StaticFilePublishResult>> Publish(PublishStaticFileRequest publishRequest)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        var staticFileSvc = RefitCreator.RestServiceFor<IUniversalStaticFileHttpClientApi>(client, sharedSecret);
        var response = await staticFileSvc.Publish(publishRequest);
        return response;
    }

    public async Task<ApiResponse<HttpContent>> PublishPublicProfileImage(PublishPublicProfileImageRequest request)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        var staticFileSvc = RefitCreator.RestServiceFor<IUniversalStaticFileHttpClientApi>(client, sharedSecret);
        return await staticFileSvc.PublishPublicProfileImage(request);
    }

    public async Task<ApiResponse<HttpContent>> PublishPublicProfileCard(PublishPublicProfileCardRequest request)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        var staticFileSvc = RefitCreator.RestServiceFor<IUniversalStaticFileHttpClientApi>(client, sharedSecret);
        return await staticFileSvc.PublishPublicProfileCard(request);
    }

    public async Task<ApiResponse<HttpContent>> GetPublicProfileCard()
    {
        var client = _factory.CreateHttpClient(_identity, out _);
        client.BaseAddress = new Uri($"{client.BaseAddress!.Scheme}://{client.BaseAddress.Host}");
        var staticFileSvc = RestService.For<IUniversalPublicStaticFileHttpClientApi>(client);
        return await staticFileSvc.GetPublicProfileCard();
    }

    public async Task<ApiResponse<HttpContent>> GetPublicProfileImage()
    {
        var client = _factory.CreateHttpClient(_identity, out _);
        client.BaseAddress = new Uri($"{client.BaseAddress!.Scheme}://{client.BaseAddress.Host}");
        var staticFileSvc = RestService.For<IUniversalPublicStaticFileHttpClientApi>(client);
        var response = await staticFileSvc.GetPublicProfileImage();
        return response;
    }

    public async Task<ApiResponse<HttpContent>> GetStaticFile(string filename)
    {
        var client = _factory.CreateHttpClient(_identity, out _);
        client.BaseAddress = new Uri($"{client.BaseAddress!.Scheme}://{client.BaseAddress.Host}");
        var staticFileSvc = RestService.For<IUniversalPublicStaticFileHttpClientApi>(client);
        return await staticFileSvc.GetStaticFile(filename);
    }
}