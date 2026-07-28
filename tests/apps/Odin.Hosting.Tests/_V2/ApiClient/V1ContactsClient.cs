using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Authentication.Owner;
using Odin.Services.Contacts;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

/// <summary>
/// Test client for the V1 contact write API. Unlike V2 (a single OwnerOrApp controller), V1 splits the
/// surface across two route bases — <c>/api/owner/v1/contacts</c> and <c>/api/apps/v1/contacts</c> —
/// so there are two Refit interfaces. The caller pairs the right route with the right auth by
/// constructing this client with the owner factory (for the <c>Owner*</c> methods) or an app factory
/// (for the <c>App*</c> methods).
/// </summary>
public class V1ContactsClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<ContactWriteResponse>> OwnerCreateAsync(CreateContactRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IOwnerContactsHttpClientV1>(client, sharedSecret);
        return await svc.Create(request);
    }

    public async Task<ApiResponse<ContactWriteResponse>> OwnerUpdateAsync(Guid uniqueId, UpdateContactRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IOwnerContactsHttpClientV1>(client, sharedSecret);
        return await svc.Update(uniqueId, request);
    }

    public async Task<ApiResponse<ContactWriteResponse>> OwnerSetAppDataAsync(Guid uniqueId, SetContactAppDataRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IOwnerContactsHttpClientV1>(client, sharedSecret);
        return await svc.SetAppData(uniqueId, request);
    }

    public async Task<ApiResponse<ContactWriteResponse>> AppCreateAsync(CreateContactRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IAppContactsHttpClientV1>(client, sharedSecret);
        return await svc.Create(request);
    }

    public async Task<ApiResponse<ContactWriteResponse>> AppSetAppDataAsync(Guid uniqueId, SetContactAppDataRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IAppContactsHttpClientV1>(client, sharedSecret);
        return await svc.SetAppData(uniqueId, request);
    }
}

public interface IOwnerContactsHttpClientV1
{
    private const string Root = OwnerApiPathConstants.ContactsV1;

    [Post(Root)]
    Task<ApiResponse<ContactWriteResponse>> Create([Body] CreateContactRequest request);

    [Put(Root + "/{uniqueId}")]
    Task<ApiResponse<ContactWriteResponse>> Update(Guid uniqueId, [Body] UpdateContactRequest request);

    [Put(Root + "/{uniqueId}/app-data")]
    Task<ApiResponse<ContactWriteResponse>> SetAppData(Guid uniqueId, [Body] SetContactAppDataRequest request);
}

public interface IAppContactsHttpClientV1
{
    private const string Root = AppApiPathConstantsV1.ContactsV1;

    [Post(Root)]
    Task<ApiResponse<ContactWriteResponse>> Create([Body] CreateContactRequest request);

    [Put(Root + "/{uniqueId}/app-data")]
    Task<ApiResponse<ContactWriteResponse>> SetAppData(Guid uniqueId, [Body] SetContactAppDataRequest request);
}
