using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Drives.Reactions;
using Odin.Services.Drives.Reactions.Redux.Group;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveGroupReactionHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.GroupReactionsByFileId;

    [Post(Endpoint)]
    Task<ApiResponse<AddReactionResult>> AddReaction(
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [Body] AddReactionRequestRedux request);

    [Delete(Endpoint)]
    Task<ApiResponse<DeleteReactionResult>> DeleteReaction(
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [Body] DeleteReactionRequestRedux request);

    [Post(Endpoint + "/toggle")]
    Task<ApiResponse<ToggleReactionResult>> ToggleReaction(
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [Body] ToggleReactionRequest request);

    [Get(Endpoint)]
    Task<ApiResponse<GetReactionsResponse>> GetAllReactions(
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [Query] GetReactionsRequestRedux request);

    [Get(Endpoint + "/summary")]
    Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile(
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [Query] GetReactionsRequestRedux request);

    [Get(Endpoint + "/by-identity")]
    Task<ApiResponse<List<string>>> GetReactionsByIdentity(
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [Query] GetReactionsByIdentityRequestRedux request);
}
