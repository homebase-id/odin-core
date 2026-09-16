using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Reactions;

/// <summary>
/// Port of <c>_Universal/DriveTests/Reactions/ReactionTestsLocalIdentityOnly</c>. Tests reactions on
/// the local identity (i.e. nothing going over transit — every <c>TransitOptions</c> here is null):
/// add, delete, counts-by-file, reactions-by-identity and all-reactions-on-file, each across the
/// Owner / React-only App / write-only Guest matrix.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> group-reaction endpoints through the in-process host: the V1-shaped
/// <see cref="UniversalDriveReactionClient"/> and <see cref="UniversalDriveApiClient"/> are reused
/// unchanged, resolved against the fixture's <c>Factory</c> via <c>InProcessApiClientFactory</c>'s
/// V1 path normalization. That makes them distinct from the sibling <see cref="LocalReactionsTests"/>,
/// which covers the V2 endpoints.
/// </remarks>
[TestFixture]
public class V1LocalReactionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Pippin];

    public static IEnumerable<object[]> OwnerAllowed()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> AppAllowedReactOnly()
    {
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.React), HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> GuestNotFound()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.NotFound];
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestNotFound))]
    public async Task CanAddReaction(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // Act
        var callerReactionClient = caller.V1.Reactions;
        const string reactionContent1 = ":k:";
        var response = await callerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return;

        await AssertIdentityHasReactionInPreview(owner, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
        await AssertIdentityHasReaction(owner, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestNotFound))]
    public async Task CanDeleteReaction(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;
        var ownerReactionClient = owner.V1.Reactions;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        const string reactionContent1 = ":k:";

        var addReactionResponse = await ownerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.That(addReactionResponse.IsSuccessStatusCode, Is.True);

        // Act
        var callerReactionClient = caller.V1.Reactions;
        var response = await callerReactionClient.DeleteReaction(new DeleteReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return;

        await AssertIdentityDoesNotHaveReactionInPreview(owner, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            reactionContent1);
        await AssertIdentityDoesNotHaveReaction(owner, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestNotFound))]
    public async Task GetReactionCountsByFile(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;
        var ownerReactionClient = owner.V1.Reactions;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        const string reactionContent1 = ":k:";
        const string reactionContent2 = ":pie:";

        var addReactionResponse1 = await ownerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.That(addReactionResponse1.IsSuccessStatusCode, Is.True);

        var addReactionResponse2 = await ownerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        Assert.That(addReactionResponse2.IsSuccessStatusCode, Is.True);

        // Act
        var callerReactionClient = caller.V1.Reactions;
        var response = await callerReactionClient.GetReactionCountsByFile(new GetReactionsRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier()
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return;

        var counts = response.Content;
        Assert.That(counts, Is.Not.Null);
        Assert.That(counts.Total, Is.EqualTo(2));
        Assert.That(counts.Reactions.SingleOrDefault(r => r.ReactionContent == reactionContent1 && r.Count == 1), Is.Not.Null);
        Assert.That(counts.Reactions.SingleOrDefault(r => r.ReactionContent == reactionContent2 && r.Count == 1), Is.Not.Null);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestNotFound))]
    public async Task CanGetReactionsByIdentity(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;
        var ownerReactionClient = owner.V1.Reactions;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        const string reactionContent1 = ":k:";
        const string reactionContent2 = ":p:";

        var addReactionResponse1 = await ownerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.That(addReactionResponse1.IsSuccessStatusCode, Is.True);

        var addReactionResponse2 = await ownerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        Assert.That(addReactionResponse2.IsSuccessStatusCode, Is.True);

        // Act
        var callerReactionClient = caller.V1.Reactions;
        var response = await callerReactionClient.GetReactionsByIdentity(new GetReactionsByIdentityRequestRedux
        {
            Identity = owner.Identity,
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier()
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return;

        var counts = response.Content;
        Assert.That(counts, Is.Not.Null);
        Assert.That(counts.Count, Is.EqualTo(2));
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestNotFound))]
    public async Task CanGetAllReactionsOnFile(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;
        var ownerReactionClient = owner.V1.Reactions;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        const string reactionContent1 = ":k:";
        const string reactionContent2 = ":p:";

        var addReactionResponse1 = await ownerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.That(addReactionResponse1.IsSuccessStatusCode, Is.True);

        var addReactionResponse2 = await ownerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        Assert.That(addReactionResponse2.IsSuccessStatusCode, Is.True);

        // Act
        var callerReactionClient = caller.V1.Reactions;
        var response = await callerReactionClient.GetReactions(new GetReactionsRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Cursor = default,
            MaxRecords = 100
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return;

        var allReactions = response.Content;
        Assert.That(allReactions, Is.Not.Null);
        Assert.That(allReactions.Reactions.Count, Is.EqualTo(2));

        Assert.That(allReactions.Reactions.SingleOrDefault(r =>
            r.ReactionContent == reactionContent1 && r.OdinId == owner.Identity && r.FileId.FileId == uploadResult.File.FileId),
            Is.Not.Null);
        Assert.That(allReactions.Reactions.SingleOrDefault(r =>
            r.ReactionContent == reactionContent2 && r.OdinId == owner.Identity && r.FileId.FileId == uploadResult.File.FileId),
            Is.Not.Null);
    }

    private static async Task AssertIdentityDoesNotHaveReactionInPreview(OwnerSession owner, FileIdentifier fileId, string reactionContent)
    {
        var client = owner.V1.Drive;
        var getHeaderResponse1 = await client.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = getHeaderResponse1.Content.SearchResults.First();
        var noMatchingInReactionPreview = file.FileMetadata.ReactionPreview.Reactions.All(pair => pair.Value.ReactionContent != reactionContent);
        Assert.That(noMatchingInReactionPreview, Is.True);
    }

    private static async Task AssertIdentityHasReactionInPreview(OwnerSession owner, FileIdentifier fileId, string reactionContent)
    {
        var client = owner.V1.Drive;
        var getHeaderResponse1 = await client.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = getHeaderResponse1.Content.SearchResults.First();
        var hasReactionInPreview = file.FileMetadata.ReactionPreview.Reactions.Any(pair => pair.Value.ReactionContent == reactionContent);
        Assert.That(hasReactionInPreview, Is.True);
    }

    private static async Task AssertIdentityHasReaction(OwnerSession owner, FileIdentifier globalTransitFileId, string reactionContent)
    {
        var client = owner.V1.Reactions;
        var getReactionsResponse = await client.GetReactions(new GetReactionsRequestRedux
            {
                File = globalTransitFileId,
            },
            FileSystemType.Standard);
        Assert.That(getReactionsResponse.Content.Reactions.Select(r => r.ReactionContent),
            Does.Contain(reactionContent));
    }

    private static async Task AssertIdentityDoesNotHaveReaction(OwnerSession owner, FileIdentifier globalTransitFileId, string reactionContent)
    {
        var client = owner.V1.Reactions;
        var getReactionsResponse = await client.GetReactions(new GetReactionsRequestRedux
            {
                File = globalTransitFileId,
            },
            FileSystemType.Standard);
        Assert.That(getReactionsResponse.Content.Reactions.Select(r => r.ReactionContent),
            Does.Not.Contain(reactionContent));
    }
}
