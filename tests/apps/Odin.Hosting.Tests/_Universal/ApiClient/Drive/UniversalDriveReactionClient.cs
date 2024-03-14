using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive;

public class UniversalDriveReactionClient(OdinId targetIdentity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> AddReaction(ExternalFileIdentifier file, string reactionContent)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var response = await svc.AddReaction(new AddReactionRequest()
        {
            File = file,
            Reaction = reactionContent
        });

        return response;
    }

    public async Task<ApiResponse<GetReactionsResponse>> GetAllReactions(ExternalFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var resp = await svc.GetReactions(file);
        return resp;
    }

    public async Task<ApiResponse<HttpContent>> DeleteReaction(string reaction, ExternalFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var response = await svc.DeleteReaction(new DeleteReactionRequest()
        {
            Reaction = reaction,
            File = file
        });

        return response;
    }

    public async Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile(ExternalFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var transitSvc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var resp = await transitSvc.DeleteReactions(new DeleteReactionRequest()
        {
            Reaction = "",
            File = file
        });

        return resp;
    }

    public async Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile(GetReactionsRequest request)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var resp = await svc.GetReactionCountsByFile(request);
        return resp;
    }

    public async Task<ApiResponse<List<string>>> GetReactionsByIdentity(TestIdentity recipient, OdinId identity1, ExternalFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var transitSvc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient>(client, ownerSharedSecret);
        var resp = await transitSvc.GetReactionsByIdentity(new GetReactionsByIdentityRequest()
        {
            Identity = identity1,
            File = file
        });

        return resp;
    }
}