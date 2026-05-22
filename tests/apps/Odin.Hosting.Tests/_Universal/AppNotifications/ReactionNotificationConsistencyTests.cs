using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Verifies the server emits a CONSISTENT view of a file's reactions over the WebSocket.
//
// Adding/removing a reaction via the group reaction path (/drive/files/group/reactions ->
// GroupReactionService) emits TWO notifications for one reaction, sharing the same versionTag:
//   - statisticsChanged (ClientNotificationType.StatisticsChanged) from UpdateReactionSummary
//   - fileModified      (ClientNotificationType.FileModified)      from UpdateLocalReactionsAsync
//
// The contract (decided): the SERVER is the source of truth for localReactions across devices,
// so every emitted notification must carry a localReactions/updated snapshot consistent with the
// server state at that event. Today statisticsChanged is built from a header read BEFORE the
// local-reactions write and BEFORE the in-memory `updated` is refreshed, so it lags. Tests 1-4
// pin that and FAIL until the server emits a consistent snapshot. Tests 5-8 are regression
// guards for current/intended behavior and pass today.
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
    // Tests 1-4: pin the server inconsistency (statisticsChanged lags). RED until the fix.
    //

    [Test]
    public async Task ReactionAdd_StatisticsChanged_AnnouncesItsOwnReaction()
    {
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            var statisticsChanged = await handler.WaitForNotification(
                ClientNotificationType.StatisticsChanged, f.Gtid, WaitTimeout);
            ClassicAssert.IsNotNull(statisticsChanged, "No statisticsChanged notification arrived for the reaction add.");

            var localReactions = statisticsChanged.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            CollectionAssert.Contains(localReactions, Reaction1,
                "statisticsChanged must announce its own reaction in localReactions, but it carries a stale snapshot " +
                "(written before the local-reactions update). Server is the source of truth; the snapshot must be consistent.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task ReactionAdd_StatisticsChanged_UpdatedReflectsItsOwnWrite()
    {
        var f = await SetupFileAsync();

        var before = await f.Owner.DriveRedux.GetFileHeader(f.File);
        var beforeUpdated = before.Content.FileMetadata.Updated;

        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            ClassicAssert.IsTrue(await handler.WaitForBoth(f.Gtid, WaitTimeout),
                "Expected both statisticsChanged and fileModified for the reaction add.");

            var statisticsChanged = handler.EventsFor(ClientNotificationType.StatisticsChanged, f.Gtid).Last();
            var fileModified = handler.EventsFor(ClientNotificationType.FileModified, f.Gtid).Last();

            ClassicAssert.Greater(statisticsChanged.Header.FileMetadata.Updated.milliseconds, beforeUpdated.milliseconds,
                "statisticsChanged.updated must reflect its own write (advance past the pre-reaction timestamp), " +
                "not carry the prior state's `updated`.");

            ClassicAssert.Greater(fileModified.Header.FileMetadata.Updated.milliseconds, beforeUpdated.milliseconds,
                "fileModified.updated must also advance past the pre-reaction timestamp.");

            // The two notifications share a versionTag, so the client cannot order them; they must
            // therefore carry the SAME localReactions snapshot. A divergence here is what caused the
            // client 'blink' (a stale statisticsChanged overwriting a fresh fileModified).
            var scLocal = statisticsChanged.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            var fmLocal = fileModified.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            CollectionAssert.AreEquivalent(fmLocal, scLocal,
                "statisticsChanged and fileModified must carry the same localReactions snapshot.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task SecondReaction_StatisticsChanged_IncludesBothReactions()
    {
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.StatisticsChanged, f.Gtid, 1, WaitTimeout));

            await AddReactionAsync(f, Reaction2);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.StatisticsChanged, f.Gtid, 2, WaitTimeout),
                "Expected a second statisticsChanged for the second reaction.");

            var second = handler.EventsFor(ClientNotificationType.StatisticsChanged, f.Gtid).Last();
            var localReactions = second.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();

            CollectionAssert.Contains(localReactions, Reaction1, "second statisticsChanged is missing the first reaction.");
            CollectionAssert.Contains(localReactions, Reaction2,
                "second statisticsChanged must include the reaction it announces; it carries a stale localReactions snapshot.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task LastStatisticsChanged_LocalReactions_MatchAuthoritativeStore_AcrossAddAddDelete()
    {
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.StatisticsChanged, f.Gtid, 1, WaitTimeout));

            await AddReactionAsync(f, Reaction2);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.StatisticsChanged, f.Gtid, 2, WaitTimeout));

            await DeleteReactionAsync(f, Reaction1);
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.StatisticsChanged, f.Gtid, 3, WaitTimeout));
            ClassicAssert.IsTrue(await handler.WaitForCount(ClientNotificationType.FileModified, f.Gtid, 3, WaitTimeout));
            await Task.Delay(SettleDelay);

            // Authoritative store (driveReactions table) for the acting identity == final truth.
            var authoritative = await GetAuthoritativeReactionsAsync(f);
            CollectionAssert.AreEquivalent(new[] { Reaction2 }, authoritative,
                "Sanity: after add r1, add r2, delete r1 the authoritative store should be exactly {r2}.");

            // The persisted header agrees with the authoritative store (this is correct today).
            var persisted = await f.Owner.DriveRedux.GetFileHeader(f.File);
            var persistedLocal = persisted.Content.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            CollectionAssert.AreEquivalent(authoritative, persistedLocal, "Persisted header localReactions diverged from authoritative store.");

            // The last fileModified is consistent (re-read after persisting) — correct today.
            var lastFileModified = handler.EventsFor(ClientNotificationType.FileModified, f.Gtid).Last();
            var fmLocal = lastFileModified.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            CollectionAssert.AreEquivalent(authoritative, fmLocal, "fileModified localReactions diverged from authoritative store.");

            // The last statisticsChanged (for the delete) must also be consistent. It lags today.
            var lastStatisticsChanged = handler.EventsFor(ClientNotificationType.StatisticsChanged, f.Gtid).Last();
            var scLocal = lastStatisticsChanged.Header.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
            CollectionAssert.AreEquivalent(authoritative, scLocal,
                "statisticsChanged must carry localReactions consistent with the server's authoritative state at emit time; " +
                "today it carries a stale snapshot taken before the local-reactions write.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    //
    // Tests 5-8: regression guards for current/intended behavior. GREEN today.
    //

    [Test]
    public async Task SingleReaction_OneSocket_EmitsExactlyOneStatisticsChanged_AndOneFileModified()
    {
        var f = await SetupFileAsync();
        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            ClassicAssert.IsTrue(await handler.WaitForBoth(f.Gtid, WaitTimeout),
                "Expected both statisticsChanged and fileModified for the reaction add.");
            await Task.Delay(SettleDelay);

            // Two DISTINCT events (one of each type) is the current design; the bug we already fixed
            // was the same fileId being delivered once-per-connected-socket. Guard against that regressing.
            ClassicAssert.AreEqual(1, handler.CountByType(ClientNotificationType.StatisticsChanged, f.Gtid),
                "Exactly one statisticsChanged expected per reaction on a single socket.");
            ClassicAssert.AreEqual(1, handler.CountByType(ClientNotificationType.FileModified, f.Gtid),
                "Exactly one fileModified expected per reaction on a single socket.");
        }
        finally
        {
            await handler.DisconnectAsync();
        }
    }

    [Test]
    public async Task ReactionAdd_BumpsFileModifiedTimestamp()
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
    public async Task Reaction_TwoSockets_EachSocketGetsOneOfEachType()
    {
        // Guards the per-connection subscription fix: with two sockets on one identity, a single
        // reaction must deliver exactly one statisticsChanged and one fileModified to EACH socket.
        var f = await SetupFileAsync();

        var socketA = new ReactionNotificationSocketHandler();
        var socketB = new ReactionNotificationSocketHandler();
        await socketA.ConnectAsync(f.Owner, f.TargetDrive);
        await socketB.ConnectAsync(f.Owner, f.TargetDrive);

        try
        {
            await AddReactionAsync(f, Reaction1);

            ClassicAssert.IsTrue(await socketA.WaitForBoth(f.Gtid, WaitTimeout), "Socket A did not receive both notifications.");
            ClassicAssert.IsTrue(await socketB.WaitForBoth(f.Gtid, WaitTimeout), "Socket B did not receive both notifications.");
            await Task.Delay(SettleDelay);

            foreach (var (name, socket) in new[] { ("A", socketA), ("B", socketB) })
            {
                ClassicAssert.AreEqual(1, socket.CountByType(ClientNotificationType.StatisticsChanged, f.Gtid),
                    $"Socket {name} should receive exactly one statisticsChanged.");
                ClassicAssert.AreEqual(1, socket.CountByType(ClientNotificationType.FileModified, f.Gtid),
                    $"Socket {name} should receive exactly one fileModified.");
            }
        }
        finally
        {
            await socketA.DisconnectAsync();
            await socketB.DisconnectAsync();
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
        FileIdentifier GroupFile);

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
            uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier());
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
