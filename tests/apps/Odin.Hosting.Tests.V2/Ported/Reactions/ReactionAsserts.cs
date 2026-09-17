using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Reactions.Redux.Group;

namespace Odin.Hosting.Tests.V2.Ported.Reactions;

/// <summary>
/// The four checks every reaction fixture in this folder makes: whether an identity's copy of a file
/// carries a reaction in its store, and whether its header's reaction preview carries it.
/// </summary>
/// <remarks>
/// <see cref="V1LocalReactionTests"/> and <see cref="ReactionDistributionTests"/> each carried their
/// own copy of all four. The preview pair is the local fixture's spelling with the precomputed
/// <c>All</c>/<c>Any</c> bools replaced by the collection constraint, so a failure prints the
/// preview's contents; the store pair is the distribution fixture's, which also pins the reaction's
/// sender.
/// <para>
/// Reads go through the <b>V1</b> endpoints (<c>owner.V1.Drive</c> / <c>owner.V1.Reactions</c>) —
/// both fixtures drive V1, and the store read is always against
/// <see cref="FileSystemType.Standard"/>: the <c>fileSystemType</c> parameter the distribution
/// fixture's copy took was never passed by any caller.
/// </para>
/// </remarks>
internal static class ReactionAsserts
{
    /// <summary>The file's reaction preview carries <paramref name="reactionContent"/>.</summary>
    public static Task AssertHasReactionInPreview(OwnerSession identity, FileIdentifier fileId, string reactionContent) =>
        AssertPreviewAsync(identity, fileId, reactionContent, shouldContain: true);

    /// <summary>The file's reaction preview does not carry <paramref name="reactionContent"/>.</summary>
    public static Task AssertDoesNotHaveReactionInPreview(OwnerSession identity, FileIdentifier fileId, string reactionContent) =>
        AssertPreviewAsync(identity, fileId, reactionContent, shouldContain: false);

    /// <summary>
    /// The file's reaction store holds exactly one <paramref name="reactionContent"/>, from
    /// <paramref name="sender"/> when one is named.
    /// </summary>
    public static Task AssertHasReaction(OwnerSession identity, FileIdentifier fileId, string reactionContent,
        OdinId? sender = null) =>
        AssertReactionCountAsync(identity, fileId, reactionContent, sender, expectedCount: 1);

    /// <summary>
    /// The file's reaction store holds no <paramref name="reactionContent"/>, from
    /// <paramref name="sender"/> when one is named.
    /// </summary>
    public static Task AssertDoesNotHaveReaction(OwnerSession identity, FileIdentifier fileId, string reactionContent,
        OdinId? sender = null) =>
        AssertReactionCountAsync(identity, fileId, reactionContent, sender, expectedCount: 0);

    private static async Task AssertPreviewAsync(OwnerSession identity, FileIdentifier fileId, string reactionContent,
        bool shouldContain)
    {
        var response = await identity.V1.Drive.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = response.Content.SearchResults.First();
        var preview = file.FileMetadata.ReactionPreview.Reactions.Select(pair => pair.Value.ReactionContent);
        Assert.That(preview, shouldContain ? Does.Contain(reactionContent) : Does.Not.Contain(reactionContent));
    }

    private static async Task AssertReactionCountAsync(OwnerSession identity, FileIdentifier fileId, string reactionContent,
        OdinId? sender, int expectedCount)
    {
        var response = await identity.V1.Reactions.GetReactions(new GetReactionsRequestRedux
            {
                File = fileId,
            },
            FileSystemType.Standard);

        Assert.That(response.Content.Reactions,
            Has.Exactly(expectedCount).Matches<Reaction>(r =>
                r.ReactionContent == reactionContent && (sender is null || r.OdinId == sender.Value)));
    }
}
