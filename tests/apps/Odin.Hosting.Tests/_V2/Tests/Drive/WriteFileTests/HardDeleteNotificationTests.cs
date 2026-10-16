using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.Tests.Drive.WriteFileTests;

/// <summary>
/// A hard delete's fileDeleted notification names the file it removed, as a soft delete's does: the deleted
/// header arrives as previousServerFileHeader, and header is null as nothing of the file is left.
///
/// In the WebScaffold framework (not the fast <c>Odin.Hosting.Tests.V2</c>) because it receives over a real
/// WebSocket, which the fast framework does not host.
/// </summary>
[TestFixture]
public class HardDeleteNotificationTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
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

    [Test]
    public async Task AHardDeleteNamesTheFileItRemoved()
    {
        var identity = TestIdentities.Pippin;
        var owner = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.DriveManager.CreateDrive(targetDrive, "Hard delete notification test drive", "", allowAnonymousReads: true);

        var upload = await owner.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload failed: {upload.StatusCode}");
        var uploaded = upload.Content!;

        var deleted = new ConcurrentQueue<ClientDriveNotification>();
        var listener = new TestOwnerWebSocketListener();
        listener.NotificationReceived += n =>
        {
            if (n.NotificationType == ClientNotificationType.FileDeleted)
            {
                deleted.Enqueue(OdinSystemSerializer.Deserialize<ClientDriveNotification>(n.Data));
            }

            return Task.CompletedTask;
        };
        await listener.ConnectAsync(identity.OdinId, owner.GetTokenContext(), new EstablishConnectionOptions { Drives = [targetDrive] });

        try
        {
            var callerContext = new OwnerTestCase(targetDrive);
            await callerContext.Initialize(owner);
            var writer = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
            var response = await writer.HardDeleteBatch(targetDrive.Alias, new HardDeleteBatchRequestV2
            {
                Files = [new FileVersionTagV2 { FileId = uploaded.File.FileId, VersionTag = uploaded.NewVersionTag }]
            });
            Assert.That(response.IsSuccessStatusCode, Is.True, $"hard delete failed: {response.StatusCode} {response.Error?.Content}");
            Assert.That(response.Content!.Results.Single().ErrorCode, Is.Null);

            var deadline = DateTime.UtcNow + WaitTimeout;
            while (deleted.IsEmpty && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            var notification = deleted.SingleOrDefault();
            Assert.That(notification, Is.Not.Null, $"no fileDeleted arrived within {WaitTimeout.TotalSeconds} s");
            Assert.That((notification!.Header, notification.TargetDrive),
                Is.EqualTo(((Odin.Services.Apps.SharedSecretEncryptedFileHeader)null, targetDrive)));
            Assert.That(
                (notification.PreviousServerFileHeader?.FileId, notification.PreviousServerFileHeader?.FileMetadata.GlobalTransitId),
                Is.EqualTo(((Guid?)uploaded.File.FileId, uploaded.GlobalTransitId)),
                "the deleted file's header must name the file");
        }
        finally
        {
            await listener.DisconnectAsync();
        }
    }
}
