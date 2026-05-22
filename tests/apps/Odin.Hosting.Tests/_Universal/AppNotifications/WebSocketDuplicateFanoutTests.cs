using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Reproduces the duplicate WebSocket fan-out bug: a single logical notification is
// delivered to a connected device once PER currently-connected socket on the same
// identity. The cause is that each WebSocket connection (its own request scope) gets
// its own AppNotificationHandler instance and therefore its own RefCountedSubscription,
// so N connections register N broker subscriptions on the shared channel. One publish
// then fans out N times, and each fan-out iterates the shared socket collection, so
// every socket receives N copies.
//
// With the bug present, a single local upload with two sockets connected delivers
// TWO FileAdded notifications to EACH socket. The fix must make it exactly one.
public class WebSocketDuplicateFanoutTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Samwise });
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

    [Test]
    public async Task LocalUpload_WithTwoSocketsOnSameIdentity_EachSocketReceivesFileAddedExactlyOnce()
    {
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        var createDrive = await owner.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "ws duplicate fan-out test drive",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(createDrive.IsSuccessStatusCode);

        // Two independent WebSocket connections for the SAME identity, both subscribed
        // to the same drive. These reuse WebSocketDrainTestSocketHandler, which records
        // EVERY FileAdded it sees (no de-duplication), so duplicates are observable.
        var socketA = new WebSocketDrainTestSocketHandler();
        var socketB = new WebSocketDrainTestSocketHandler();
        await socketA.ConnectAsync(owner, targetDrive);
        await socketB.ConnectAsync(owner, targetDrive);

        try
        {
            // Exactly one local upload -> exactly one DriveFileAddedNotification.
            var metadata = SampleMetadataData.Create(fileType: 9911);
            var upload = await owner.DriveRedux.UploadNewMetadata(targetDrive, metadata);
            ClassicAssert.IsTrue(upload.IsSuccessStatusCode, $"upload failed: {upload.StatusCode}");

            var gtid = upload.Content.GlobalTransitIdFileIdentifier.GlobalTransitId;

            // Wait until each socket has seen the FileAdded at least once...
            ClassicAssert.IsTrue(await socketA.WaitForFileAdded(gtid, TimeSpan.FromSeconds(15)),
                "Socket A never received FileAdded for the uploaded file.");
            ClassicAssert.IsTrue(await socketB.WaitForFileAdded(gtid, TimeSpan.FromSeconds(15)),
                "Socket B never received FileAdded for the uploaded file.");

            // ...then settle. Duplicates originate from the same publish and arrive
            // back-to-back, so a short window is plenty to catch them before counting.
            await Task.Delay(TimeSpan.FromSeconds(2));

            var countA = socketA.FileAddedEvents.Count(e => e.Header.FileMetadata?.GlobalTransitId == gtid);
            var countB = socketB.FileAddedEvents.Count(e => e.Header.FileMetadata?.GlobalTransitId == gtid);

            ClassicAssert.AreEqual(1, countA,
                $"Socket A received {countA} copies of the single FileAdded (expected 1). " +
                "Duplicate WebSocket fan-out: each connection registers its own pub/sub subscription.");
            ClassicAssert.AreEqual(1, countB,
                $"Socket B received {countB} copies of the single FileAdded (expected 1). " +
                "Duplicate WebSocket fan-out: each connection registers its own pub/sub subscription.");
        }
        finally
        {
            await socketA.DisconnectAsync();
            await socketB.DisconnectAsync();
        }
    }
}
