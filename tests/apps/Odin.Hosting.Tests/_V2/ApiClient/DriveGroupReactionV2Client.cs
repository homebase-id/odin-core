using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Drives.Reactions;
using Odin.Services.Drives.Reactions.Redux.Group;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DriveGroupReactionV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<AddReactionResult>> AddReactionAsync(Guid driveId, Guid fileId, string reaction)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveGroupReactionHttpClientApiV2>(client, sharedSecret);
        return await svc.AddReaction(driveId, fileId, new AddReactionRequestRedux
        {
            Reaction = reaction,
            TransitOptions = null
        });
    }

    public async Task<ApiResponse<DeleteReactionResult>> DeleteReactionAsync(Guid driveId, Guid fileId, string reaction)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveGroupReactionHttpClientApiV2>(client, sharedSecret);
        return await svc.DeleteReaction(driveId, fileId, new DeleteReactionRequestRedux
        {
            Reaction = reaction,
            TransitOptions = null
        });
    }

    public async Task<ApiResponse<ToggleReactionResult>> ToggleReactionAsync(Guid driveId, Guid fileId, string reaction)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveGroupReactionHttpClientApiV2>(client, sharedSecret);
        return await svc.ToggleReaction(driveId, fileId, new ToggleReactionRequest
        {
            Reaction = reaction,
            TransitOptions = null
        });
    }

    public async Task<ApiResponse<GetReactionsResponse>> GetAllReactionsAsync(Guid driveId, Guid fileId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveGroupReactionHttpClientApiV2>(client, sharedSecret);
        return await svc.GetAllReactions(driveId, fileId, new GetReactionsRequestRedux());
    }

    public async Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFileAsync(Guid driveId, Guid fileId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveGroupReactionHttpClientApiV2>(client, sharedSecret);
        return await svc.GetReactionCountsByFile(driveId, fileId, new GetReactionsRequestRedux());
    }

    public async Task<ApiResponse<List<string>>> GetReactionsByIdentityAsync(Guid driveId, Guid fileId, string odinId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveGroupReactionHttpClientApiV2>(client, sharedSecret);
        return await svc.GetReactionsByIdentity(driveId, fileId, new GetReactionsByIdentityRequestRedux
        {
            Identity = odinId
        });
    }
}
