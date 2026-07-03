using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.UnifiedV2.Profile;
using Odin.Services.Profile;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2ProfileClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<ProfileAttributeWriteResponse>> SetAttributeAsync(SetProfileAttributeRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IProfileHttpClientApiV2>(client, sharedSecret);
        return await svc.SetAttribute(request);
    }

    public async Task<ApiResponse<ProfileAttributeWriteResponse>> SetPhotoAttributeAsync(SetPhotoAttributeRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IProfileHttpClientApiV2>(client, sharedSecret);
        return await svc.SetPhotoAttribute(request);
    }

    public async Task<ApiResponse<HttpContent>> DeleteAttributeAsync(Guid id, Guid versionTag)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IProfileHttpClientApiV2>(client, sharedSecret);
        return await svc.DeleteAttribute(id, versionTag);
    }
}
