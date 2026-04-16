using System;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

/// <summary>
/// Refit interface for V2 non-group (direct) reaction endpoints.
/// These call ReactionContentService directly, bypassing GroupReactionService,
/// so they do NOT update LocalReactions. Useful in tests to create stale local entries.
/// </summary>
public interface IDriveDirectReactionHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.ReactionsByFileId;

    [Post(Endpoint + "/add")]
    Task<IApiResponse> AddReaction(
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [Body] AddReactionRequest request);

    [Post(Endpoint + "/delete")]
    Task<IApiResponse> DeleteReaction(
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [Body] DeleteReactionRequest request);
}
