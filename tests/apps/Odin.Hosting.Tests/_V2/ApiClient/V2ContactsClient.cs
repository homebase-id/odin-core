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
    public async Task<ApiResponse<ContactWriteResponse>> CreateAsync(CreateContactRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IContactsHttpClientApiV2>(client, sharedSecret);
        return await svc.Create(request);
    }

    public async Task<ApiResponse<ContactWriteResponse>> UpdateAsync(Guid uniqueId, UpdateContactRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IContactsHttpClientApiV2>(client, sharedSecret);
        return await svc.Update(uniqueId, request);
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
