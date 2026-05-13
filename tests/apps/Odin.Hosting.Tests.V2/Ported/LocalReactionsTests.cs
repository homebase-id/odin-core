using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported;

/// <summary>
/// Port of <c>_V2/Tests/Drive/Reactions/LocalReactionsTests</c>. All ten owner-only tests:
/// add / delete / toggle / duplicate behaviour for the group-reaction API, plus the two
/// stale-local-reaction self-correction scenarios (delete via group API and toggle via group API)
/// that hand-fire the direct delete endpoint to desync server from local. Validates the
/// <c>LocalAppData.LocalReactions</c> projection.
/// </summary>
[TestFixture]
public class LocalReactionsTests : V2Fixture
{
    [Test]
    public async Task AddReactionPopulatesLocalReactions()
    {
        var (owner, file) = await SeedFileAsync();

        const string reaction = ":thumbsup:";
        var addResponse = await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, reaction);
        Assert.That(addResponse.IsSuccessStatusCode, Is.True);

        var local = await GetLocalReactionsAsync(owner, file);
        Assert.That(local, Does.Contain(reaction));
    }

    [Test]
    public async Task DeleteReactionRemovesFromLocalReactions()
    {
        var (owner, file) = await SeedFileAsync();
        const string reaction = ":heart:";

        Assert.That((await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, reaction)).IsSuccessStatusCode, Is.True);
        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Contain(reaction));

        Assert.That((await owner.Drives.Reactions.DeleteReactionAsync(file.DriveId, file.FileId, reaction)).IsSuccessStatusCode, Is.True);
        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Not.Contain(reaction));
    }

    [Test]
    public async Task ToggleReactionAddsToLocalReactions()
    {
        var (owner, file) = await SeedFileAsync();
        const string reaction = ":fire:";

        var toggleResponse = await owner.Drives.Reactions.ToggleReactionAsync(file.DriveId, file.FileId, reaction);
        Assert.That(toggleResponse.IsSuccessStatusCode, Is.True);
        Assert.That(toggleResponse.Content!.ResultType, Is.EqualTo(ToggleReactionResultType.Added));

        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Contain(reaction));
    }

    [Test]
    public async Task ToggleReactionRemovesFromLocalReactions()
    {
        var (owner, file) = await SeedFileAsync();
        const string reaction = ":star:";

        var toggle1 = await owner.Drives.Reactions.ToggleReactionAsync(file.DriveId, file.FileId, reaction);
        Assert.That(toggle1.IsSuccessStatusCode, Is.True);
        Assert.That(toggle1.Content!.ResultType, Is.EqualTo(ToggleReactionResultType.Added));

        var toggle2 = await owner.Drives.Reactions.ToggleReactionAsync(file.DriveId, file.FileId, reaction);
        Assert.That(toggle2.IsSuccessStatusCode, Is.True);
        Assert.That(toggle2.Content!.ResultType, Is.EqualTo(ToggleReactionResultType.Deleted));

        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Not.Contain(reaction));
    }

    [Test]
    public async Task AddMultipleReactionsPopulatesLocalReactions()
    {
        var (owner, file) = await SeedFileAsync();
        var reactions = new[] { ":thumbsup:", ":heart:", ":fire:" };

        foreach (var reaction in reactions)
        {
            Assert.That((await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, reaction)).IsSuccessStatusCode, Is.True);
        }

        var local = await GetLocalReactionsAsync(owner, file);
        Assert.That(local.Count, Is.EqualTo(3));
        foreach (var reaction in reactions)
        {
            Assert.That(local, Does.Contain(reaction));
        }
    }

    [Test]
    public async Task AddDuplicateReactionDoesNotDuplicate()
    {
        var (owner, file) = await SeedFileAsync();
        const string reaction = ":clap:";

        // Second add may fail at the DB level (unique constraint); LocalReactions should still
        // contain the reaction exactly once.
        await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, reaction);
        await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, reaction);

        var local = await GetLocalReactionsAsync(owner, file);
        Assert.That(local.Count(r => r == reaction), Is.EqualTo(1));
    }

    [Test]
    public async Task LocalReactionsSurvivesLocalMetadataContentUpdate()
    {
        var (owner, file) = await SeedFileAsync();
        const string reaction = ":rocket:";

        Assert.That((await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, reaction)).IsSuccessStatusCode, Is.True);

        var localVersionTag = await GetLocalVersionTagAsync(owner, file);
        var updateResponse = await owner.Drives.Writer.UpdateLocalAppMetadataContent(file.DriveId, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = localVersionTag, Content = "some updated content" });
        Assert.That(updateResponse.IsSuccessStatusCode, Is.True);

        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Contain(reaction));
    }

    [Test]
    public async Task LocalReactionsSurvivesLocalMetadataTagUpdate()
    {
        var (owner, file) = await SeedFileAsync();
        const string reaction = ":wave:";

        Assert.That((await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, reaction)).IsSuccessStatusCode, Is.True);

        var localVersionTag = await GetLocalVersionTagAsync(owner, file);
        var updateResponse = await owner.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = localVersionTag, Tags = [Guid.NewGuid(), Guid.NewGuid()] });
        Assert.That(updateResponse.IsSuccessStatusCode, Is.True);

        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Contain(reaction));
    }

    [Test]
    public async Task StaleLocalReactionSelfCorrectsByDelete()
    {
        // LocalReactions has a stale entry that no longer exists on the server. A group-API
        // delete should remove the local entry even though the server has nothing to delete.
        var (owner, file) = await SeedFileAsync();
        const string staleReaction = ":stale:";

        Assert.That((await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, staleReaction)).IsSuccessStatusCode, Is.True);
        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Contain(staleReaction));

        await DeleteReactionDirectAsync(owner, file, staleReaction);

        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Contain(staleReaction),
            "stale local entry should remain after direct (non-group) delete");

        Assert.That((await owner.Drives.Reactions.DeleteReactionAsync(file.DriveId, file.FileId, staleReaction)).IsSuccessStatusCode, Is.True);
        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Not.Contain(staleReaction),
            "group delete should self-correct the stale local entry");
    }

    [Test]
    public async Task StaleLocalReactionSelfCorrectsByToggle()
    {
        // LocalReactions has a stale entry. Toggling via group API: server doesn't have it so
        // toggle re-ADDS; local already has it. Both end up in sync.
        var (owner, file) = await SeedFileAsync();
        const string staleReaction = ":stale:";

        Assert.That((await owner.Drives.Reactions.AddReactionAsync(file.DriveId, file.FileId, staleReaction)).IsSuccessStatusCode, Is.True);
        await DeleteReactionDirectAsync(owner, file, staleReaction);
        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Contain(staleReaction));

        var toggleResponse = await owner.Drives.Reactions.ToggleReactionAsync(file.DriveId, file.FileId, staleReaction);
        Assert.That(toggleResponse.IsSuccessStatusCode, Is.True);
        Assert.That(toggleResponse.Content!.ResultType, Is.EqualTo(ToggleReactionResultType.Added),
            "toggle should re-add since server didn't have it");

        Assert.That(await GetLocalReactionsAsync(owner, file), Does.Contain(staleReaction));

        var countsResponse = await owner.Drives.Reactions.GetReactionCountsByFileAsync(file.DriveId, file.FileId);
        Assert.That(countsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(countsResponse.Content!.Reactions.Any(r => r.ReactionContent == staleReaction), Is.True,
            "server should have the reaction after toggle re-added it");
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    private async Task<(OwnerSession Owner, FileRef File)> SeedFileAsync()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Test Drive");

        var upload = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, SampleMetadataData.Create(fileType: 100));
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"seed upload failed: {upload.StatusCode}");
        return (owner, new FileRef(upload.Content!.DriveId, upload.Content.FileId));
    }

    private static async Task<List<string>> GetLocalReactionsAsync(OwnerSession owner, FileRef file)
    {
        var header = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"header fetch failed: {header.StatusCode}");
        return header.Content!.FileMetadata.LocalAppData?.LocalReactions ?? [];
    }

    private static async Task<Guid> GetLocalVersionTagAsync(OwnerSession owner, FileRef file)
    {
        var header = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(header.IsSuccessStatusCode, Is.True);
        return header.Content!.FileMetadata.LocalAppData?.VersionTag ?? Guid.Empty;
    }

    /// <summary>
    /// Bypasses the group reaction endpoint to delete from the server only — the V2 surface used
    /// in the stale-local-reaction scenarios. There's no dedicated wrapper for this in the V2
    /// client tree, so we wire Refit directly off the owner's factory here.
    /// </summary>
    private static async Task DeleteReactionDirectAsync(OwnerSession owner, FileRef file, string reaction)
    {
        var http = owner.Factory.CreateHttpClient(owner.Identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveDirectReactionHttpClientApiV2>(http, sharedSecret);
        var response = await svc.DeleteReaction(file.DriveId, file.FileId, new DeleteReactionRequest { Reaction = reaction });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"direct delete failed: {response.StatusCode}");
    }

    private readonly record struct FileRef(Guid DriveId, Guid FileId);
}
