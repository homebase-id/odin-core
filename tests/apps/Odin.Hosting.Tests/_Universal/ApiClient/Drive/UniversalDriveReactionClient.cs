using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base.Drive.ReactionsRedux;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive;

public class UniversalDriveReactionClient(OdinId targetIdentity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> AddReaction(AddReactionRequestRedux request)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var response = await svc.AddReaction(request);

        return response;
    }

    public async Task<ApiResponse<GetReactionsResponse>> GetReactions(GetReactionsRequestRedux request, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, sharedSecret);
        var response = await svc.GetReactions(request);
        return response;
    }

    public async Task<ApiResponse<HttpContent>> DeleteReaction(DeleteReactionRequestRedux request)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var response = await svc.DeleteReaction(request);

        return response;
    }

    public async Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile(GetReactionsRequestRedux request)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var response = await svc.GetReactionCountsByFile(request);
        return response;
    }

    public async Task<ApiResponse<List<string>>> GetReactionsByIdentity(GetReactionsByIdentityRequestRedux request)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var transitSvc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var response = await transitSvc.GetReactionsByIdentity(request);

        return response;
    }
}