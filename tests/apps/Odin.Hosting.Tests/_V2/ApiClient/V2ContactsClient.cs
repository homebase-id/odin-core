using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Contacts;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2ContactsClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<UpsertContactResponse>> UpsertAsync(UpsertContactRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IContactsHttpClientApiV2>(client, sharedSecret);
        return await svc.Upsert(request);
    }

    public async Task<ApiResponse<HttpContent>> DeleteAsync(Guid uniqueId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IContactsHttpClientApiV2>(client, sharedSecret);
        return await svc.Delete(uniqueId);
    }

    public async Task<ApiResponse<HttpContent>> SyncAsync(string odinId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IContactsHttpClientApiV2>(client, sharedSecret);
        return await svc.Sync(odinId);
    }
}
