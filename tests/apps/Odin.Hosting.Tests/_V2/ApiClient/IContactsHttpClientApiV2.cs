using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Contacts;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IContactsHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Contacts;

    [Post(Root)]
    Task<ApiResponse<UpsertContactResponse>> Upsert([Body] UpsertContactRequest request);

    [Delete(Root + "/{uniqueId}")]
    Task<ApiResponse<HttpContent>> Delete(Guid uniqueId);

    [Post(Root + "/sync/{odinId}")]
    Task<ApiResponse<HttpContent>> Sync(string odinId);
}
