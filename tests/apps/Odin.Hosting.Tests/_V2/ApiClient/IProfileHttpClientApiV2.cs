using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Profile;
using Odin.Services.Profile;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IProfileHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Profile;

    [Put(Root + "/attributes")]
    Task<ApiResponse<ProfileAttributeWriteResponse>> SetAttribute([Body] SetProfileAttributeRequest request);

    [Delete(Root + "/attributes/{id}")]
    Task<ApiResponse<HttpContent>> DeleteAttribute(Guid id, Guid versionTag);
}
