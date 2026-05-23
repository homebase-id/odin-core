using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// A reaction (and a comment) emits a SINGLE full-header WebSocket notification, of type
// fileModified. Mechanics:
//   - The reaction summary update (ReactionPreviewUpdatedNotification) is surfaced to clients as
//     fileModified (formerly statisticsChanged) and carries the fresh full header
//     (reactionSummary + localReactions + updated).
//   - The local-reactions DriveFileChangedNotification (also fileModified) is suppressed from the
//     WS broadcast (IgnoreWebSocketNotification) so the header isn't sent twice; its MediatR event
//     still fires for cache/feed.
// So for a reaction the client receives exactly one fileModified and zero statisticsChanged.
public class ReactionNotificationConsistencyTests
{
    private WebScaffold _scaffold;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    // Time to let any late/duplicate notifications land before counting.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private const string Reaction1 = ":thumbsup:";
    private const string Reaction2 = ":open_mouth:";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Pippin });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    //
    // The single full-header fileModified per reaction, and its consistency.
    //

    [Test]
    public async Task ReactionAdd_EmitsExactlyOneFileModified_AndNoStatisticsChanged()
    {
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            var fileModified = await handler.WaitForNotification(ClientNotificationType.FileModified, f.Gtid, WaitTimeout);
            ClassicAssert.IsNotNull(fileModified, "No fileModified notification arrived for the reaction add.");
            await Task.Delay(SettleDelay);

            ClassicAssert.AreEqual(1, handler.CountByType(ClientNotificationType.FileModified, f.Gtid),
                "A reaction should send the full header exactly once (one fileModified).");
            ClassicAssert.AreEqual(0, handler.CountByType(ClientNotificationType.StatisticsChanged, f.Gtid),
                "statisticsChanged is retired; a reaction must not send it.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task ReactionAdd_FileModified_AnnouncesItsOwnReaction()
    {
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            var fileModified = await handler.WaitForNotification(ClientNotificationType.FileModified, f.Gtid, WaitTimeout);
            ClassicAssert.IsNotNull(fileModified, "No fileModified notification arrived for the reaction add.");

            var localReactions = fileModified.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            CollectionAssert.Contains(localReactions, Reaction1,
                "The reaction's fileModified must carry localReactions including the reaction it announces.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task ReactionAdd_FileModified_UpdatedReflectsItsOwnWrite()
    {
        var f = await SetupFileAsync();

        var before = await f.Owner.DriveRedux.GetFileHeader(f.File);
        var beforeUpdated = before.Content.FileMetadata.Updated;

        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            var fileModified = await handler.WaitForNotification(ClientNotificationType.FileModified, f.Gtid, WaitTimeout);
            ClassicAssert.IsNotNull(fileModified, "No fileModified notification arrived for the reaction add.");

            ClassicAssert.Greater(fileModified.Header.FileMetadata.Updated.milliseconds, beforeUpdated.milliseconds,
                "The reaction's fileModified must carry an `updated` that advanced past the pre-reaction timestamp.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task SecondReaction_FileModified_IncludesBothReactions()
    {
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.FileModified, f.Gtid, 1, WaitTimeout));

            await AddReactionAsync(f, Reaction2);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.FileModified, f.Gtid, 2, WaitTimeout),
                "Expected a second fileModified for the second reaction.");

            var second = handler.EventsFor(ClientNotificationType.FileModified, f.Gtid).Last();
            var localReactions = second.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();

            CollectionAssert.Contains(localReactions, Reaction1, "second fileModified is missing the first reaction.");
            CollectionAssert.Contains(localReactions, Reaction2,
                "second fileModified must include the reaction it announces.");

            ClassicAssert.AreEqual(0, handler.CountByType(ClientNotificationType.StatisticsChanged, f.Gtid),
                "statisticsChanged is retired; reactions must not send it.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task LastFileModified_LocalReactions_MatchAuthoritativeStore_AcrossAddAddDelete()
    {
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.FileModified, f.Gtid, 1, WaitTimeout));

            await AddReactionAsync(f, Reaction2);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.FileModified, f.Gtid, 2, WaitTimeout));

            await DeleteReactionAsync(f, Reaction1);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.FileModified, f.Gtid, 3, WaitTimeout));
            await Task.Delay(SettleDelay);

            // Authoritative store (driveReactions table) for the acting identity == final truth.
            var authoritative = await GetAuthoritativeReactionsAsync(f);
            CollectionAssert.AreEquivalent(new[] { Reaction2 }, authoritative,
                "Sanity: after add r1, add r2, delete r1 the authoritative store should be exactly {r2}.");

            // The persisted header agrees with the authoritative store.
            var persisted = await f.Owner.DriveRedux.GetFileHeader(f.File);
            var persistedLocal = persisted.Content.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            CollectionAssert.AreEquivalent(authoritative, persistedLocal, "Persisted header localReactions diverged from authoritative store.");

            // The last fileModified must carry the consistent, current localReactions.
            var lastFileModified = handler.EventsFor(ClientNotificationType.FileModified, f.Gtid).Last();
            var lastLocal = lastFileModified.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            CollectionAssert.AreEquivalent(authoritative, lastLocal,
                "The last fileModified must carry localReactions consistent with the server's authoritative state.");

            ClassicAssert.AreEqual(0, handler.CountByType(ClientNotificationType.StatisticsChanged, f.Gtid),
                "statisticsChanged is retired; reactions must not send it.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task ReactionAdd_BumpsUpdatedTimestamp()
    {
        // Characterization: a reaction (a non-content change) bumps the file's `updated`/modified
        // timestamp. Documented so any future change to this behavior is deliberate.
        var f = await SetupFileAsync();

        var before = await f.Owner.DriveRedux.GetFileHeader(f.File);
        var beforeUpdated = before.Content.FileMetadata.Updated;

        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            var fileModified = await handler.WaitForNotification(ClientNotificationType.FileModified, f.Gtid, WaitTimeout);
            ClassicAssert.IsNotNull(fileModified, "No fileModified notification arrived for the reaction add.");

            ClassicAssert.Greater(fileModified.Header.FileMetadata.Updated.milliseconds, beforeUpdated.milliseconds,
                "A reaction is expected to bump the file's `updated` timestamp.");

            var after = await f.Owner.DriveRedux.GetFileHeader(f.File);
            ClassicAssert.Greater(after.Content.FileMetadata.Updated.milliseconds, beforeUpdated.milliseconds,
                "The persisted file `updated` should advance after a reaction.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task ReactionPreview_DictionaryKey_IsSha256OfReactionContent()
    {
        // Documents the server's reaction-preview map key format (a SHA256-reduced GUID of the
        // reaction content), which differs from the client's key format. Harmless today because
        // rendering reads ReactionContent, not the key.
        var f = await SetupFileAsync();

        await AddReactionAsync(f, Reaction1);

        var header = await f.Owner.DriveRedux.GetFileHeader(f.File);
        var reactions = header.Content.FileMetadata.ReactionPreview.Reactions;

        var expectedKey = ByteArrayUtil.ReduceSHA256Hash(Reaction1);
        ClassicAssert.IsTrue(reactions.ContainsKey(expectedKey),
            "ReactionPreview should be keyed by ReduceSHA256Hash(reactionContent).");

        var preview = reactions[expectedKey];
        ClassicAssert.AreEqual(Reaction1, preview.ReactionContent);
        ClassicAssert.AreEqual(expectedKey, preview.Key);
    }

    [Test]
    public async Task Reaction_TwoSockets_EachSocketGetsOneFileModified_AndNoStatisticsChanged()
    {
        // Each connected socket on the identity receives exactly one fileModified (and no
        // statisticsChanged) for a single reaction. Also guards the per-connection subscription fix.
        var f = await SetupFileAsync();

        var socketA = new ReactionNotificationSocketHandler();
        var socketB = new ReactionNotificationSocketHandler();
        await socketA.ConnectAsync(f.Owner, f.TargetDrive);
        await socketB.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            ClassicAssert.IsNotNull(await socketA.WaitForNotification(ClientNotificationType.FileModified, f.Gtid, WaitTimeout),
                "Socket A did not receive a fileModified.");
            ClassicAssert.IsNotNull(await socketB.WaitForNotification(ClientNotificationType.FileModified, f.Gtid, WaitTimeout),
                "Socket B did not receive a fileModified.");
            await Task.Delay(SettleDelay);

            foreach (var (name, socket) in new[] { ("A", socketA), ("B", socketB) })
            {
                ClassicAssert.AreEqual(1, socket.CountByType(ClientNotificationType.FileModified, f.Gtid),
                    $"Socket {name} should receive exactly one fileModified.");
                ClassicAssert.AreEqual(0, socket.CountByType(ClientNotificationType.StatisticsChanged, f.Gtid),
                    $"Socket {name} should receive zero statisticsChanged.");
            }
        }
        finally
        {
            await socketA.DisconnectAsync();
            await socketB.DisconnectAsync();
        }
    }

    [Test]
    public async Task Comment_EmitsFileModified_OnTargetFile_AndNoStatisticsChanged()
    {
        // A comment referencing a file updates the target's reaction preview, which the target file's
        // owner receives as a single fileModified (was statisticsChanged) with no extra fileModified
        // (comments never write localReactions).
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            var commentMetadata = SampleMetadataData.Create(fileType: 1111);
            commentMetadata.ReferencedFile = f.ReferencedFile;

            var commentUpload = await f.Owner.DriveRedux.UploadNewMetadata(f.TargetDrive, commentMetadata, FileSystemType.Comment);
            ClassicAssert.IsTrue(commentUpload.IsSuccessStatusCode, $"comment upload failed: {commentUpload.StatusCode}");

            var fileModified = await handler.WaitForNotification(ClientNotificationType.FileModified, f.Gtid, WaitTimeout);
            ClassicAssert.IsNotNull(fileModified, "Target file did not receive fileModified after a comment.");
            await Task.Delay(SettleDelay);

            ClassicAssert.AreEqual(1, handler.CountByType(ClientNotificationType.FileModified, f.Gtid),
                "A comment should produce exactly one fileModified on the target file.");
            ClassicAssert.AreEqual(0, handler.CountByType(ClientNotificationType.StatisticsChanged, f.Gtid),
                "statisticsChanged is retired; a comment must not send it.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    //
    // Helpers
    //

    private sealed record ReactionTestFile(
        OwnerApiClientRedux Owner,
        TargetDrive TargetDrive,
        Guid Gtid,
        ExternalFileIdentifier File,
        FileIdentifier GroupFile,
        GlobalTransitIdFileIdentifier ReferencedFile);

    private async Task<ReactionTestFile> SetupFileAsync()
    {
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.DriveManager.CreateDrive(targetDrive, "Reaction notification test drive", "", allowAnonymousReads: true);

        var metadata = SampleMetadataData.Create(fileType: 100);
        var uploadResponse = await owner.DriveRedux.UploadNewMetadata(targetDrive, metadata);
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode, $"upload failed: {uploadResponse.StatusCode}");

        var uploadResult = uploadResponse.Content;
        return new ReactionTestFile(
            owner,
            targetDrive,
            uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId,
            uploadResult.File,
            uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            uploadResult.GlobalTransitIdFileIdentifier);
    }

    private static async Task AddReactionAsync(ReactionTestFile f, string reaction)
    {
        var response = await f.Owner.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = f.GroupFile,
            Reaction = reaction,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"AddReaction failed: {response.StatusCode}");
    }

    private static async Task DeleteReactionAsync(ReactionTestFile f, string reaction)
    {
        var response = await f.Owner.Reactions.DeleteReaction(new DeleteReactionRequestRedux
        {
            File = f.GroupFile,
            Reaction = reaction,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"DeleteReaction failed: {response.StatusCode}");
    }

    private static async Task<List<string>> GetAuthoritativeReactionsAsync(ReactionTestFile f)
    {
        var response = await f.Owner.Reactions.GetReactionsByIdentity(new GetReactionsByIdentityRequestRedux
        {
            Identity = f.Owner.Identity.OdinId,
            File = f.GroupFile
        });
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"GetReactionsByIdentity failed: {response.StatusCode}");
        return response.Content ?? new List<string>();
    }
}
