using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.App;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.Performance;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Peer.PeerAppNotificationsWebSocket
{
    /// <summary>
    /// Tests that we can receive app notifications from a remote identity via peer
    /// </summary>
    public class PeerAppNotificationChatSpamTests
    {
        private WebScaffold _scaffold;

        private static readonly object _lock = new();

        private readonly List<Guid> _filesSentByFrodo = new();

        // private readonly List<Guid> _readReceiptsReceivedByFrodo = new();
        private readonly List<Guid> _filesReceivedBySam = new();
        // private readonly List<Guid> _readReceiptsSentBySam = new();

        private const int ProcessInboxBatchSize = 10;
        private const int NotificationBatchSize = 10;
        private const int NotificationWaitTime = 30;

        private readonly PeerAppNotificationSocketHandler _frodoSocketHandler = new(NotificationBatchSize, NotificationWaitTime);
        private readonly PeerAppNotificationSocketHandler _samSocketHandler = new(NotificationBatchSize, NotificationWaitTime);

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);

            var env = new Dictionary<string, string>
            {
                { "Job__BackgroundJobStartDelaySeconds", "0" },
                { "Job__CronProcessingInterval", "1" },
                { "Job__EnableJobBackgroundService", "true" },
                { "Job__Enabled", "true" },
            };

            _scaffold.RunBeforeAnyTests(envOverrides: env);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }

        [Test, Explicit]
        public async Task CommunityChatSpamTestEndToEnd_OnlySuccessScenarios()
        {
            /*
             * Frodo and sam are chatting; they get into a heated debate and chat goes really fast
             * As they chat, items are sent out of the outbox to the recipient
             * As the recipient receives items, the recipient sends back a read-receipt; which also goes into the outbox
             * I need to ensure the outbox is being emptied and at the end of the test; no items remain (with in X minutes)
             */

            // Setup
            var targetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

            var hostIdentity = _scaffold.CreateOwnerApiClientRedux(TestIdentities.TomBombadil); //todo: change to collab identity
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            var (frodoAppApi, samAppApi) = await PrepareScenario(hostIdentity, frodo, sam, targetDrive);

            await SetupSockets(hostIdentity, frodoAppApi, samAppApi, [targetDrive]);

            // Act
            await SendBarrage(hostIdentity, frodo, sam, targetDrive, maxThreads: 5, iterations: 50);

            await WaitForEmptyOutboxes(hostIdentity, frodo, sam, targetDrive, TimeSpan.FromSeconds(60));

            await WaitForEmptyInboxes(hostIdentity, frodo, sam, targetDrive, TimeSpan.FromSeconds(90));

            Console.WriteLine("Parameters:");

            Console.WriteLine("\tApp Notifications:");
            Console.WriteLine($"\t\tBatch Size: {NotificationBatchSize}");
            Console.WriteLine($"\t\tWait Time (ms): {NotificationWaitTime}");

            Console.WriteLine("\tInbox:");
            Console.WriteLine($"\t\tProcess Batch Size: {ProcessInboxBatchSize}");

            Console.WriteLine("Test Metrics:");
            Console.WriteLine($"\tSent Files: {_filesSentByFrodo.Count}");
            Console.WriteLine($"\tReceived Files:{_filesReceivedBySam.Count}");
            // Console.WriteLine($"\tRead-receipts Sent: {_readReceiptsSentBySam.Count}");
            // Console.WriteLine($"\tRead-receipts received: {_readReceiptsReceivedByFrodo.Count}");

            PerformanceCounter.WriteCounters();

            // Wait long enough for all notifications to be flushed
            await Task.Delay(TimeSpan.FromSeconds(10));

            CollectionAssert.AreEquivalent(_filesSentByFrodo, _filesReceivedBySam);
            // CollectionAssert.AreEquivalent(_filesReceivedBySam, _readReceiptsSentBySam,
            //     "mismatch in number of read-receipts send by sam to the files received");

            // CollectionAssert.AreEquivalent(_readReceiptsSentBySam, _readReceiptsReceivedByFrodo);

            await Shutdown(hostIdentity, frodo, sam);
        }

        private async Task SetupSockets(OwnerApiClientRedux hostIdentity, AppApiClientRedux frodo, AppApiClientRedux sam,
            List<TargetDrive> targetDrives)
        {
            var getTokenRequest = new GetRemoteTokenRequest()
            {
                Identity = hostIdentity.Identity.OdinId
            };

            //create remote tokens for listening to peer app notifications
            var frodoGetTokenResponse = await frodo.PeerAppNotification.GetRemoteNotificationToken(getTokenRequest);
            var frodoToken = frodoGetTokenResponse.Content!.ToCat();
            Assert.IsTrue(frodoGetTokenResponse.IsSuccessStatusCode);

            var samGetTokenResponse = await sam.PeerAppNotification.GetRemoteNotificationToken(getTokenRequest);
            var samToken = samGetTokenResponse.Content!.ToCat();
            Assert.IsTrue(samGetTokenResponse.IsSuccessStatusCode);

            await _samSocketHandler.ConnectAsync(hostIdentity.Identity.OdinId, frodoToken, targetDrives);
            _samSocketHandler.FileAdded += SamSocketHandlerOnFileAdded;

            await _frodoSocketHandler.ConnectAsync(hostIdentity.Identity.OdinId, samToken, targetDrives);
            _frodoSocketHandler.FileModified += FrodoSocketHandlerOnFileModified;
        }

        private void FrodoSocketHandlerOnFileModified(object sender, (TargetDrive targetDrive, SharedSecretEncryptedFileHeader header) e)
        {
            //validate sam marked ita s ready
            if (e.header.ServerMetadata.TransferHistory.Recipients.TryGetValue(TestIdentities.Samwise.OdinId, out var value))
            {
                if (value.IsReadByRecipient)
                {
                    // _readReceiptsReceivedByFrodo.Add(e.header.FileMetadata.GlobalTransitId.GetValueOrDefault());
                }
            }
        }

        private void SamSocketHandlerOnFileAdded(object sender, (TargetDrive targetDrive, SharedSecretEncryptedFileHeader header) e)
        {
            _filesReceivedBySam.Add(e.header.FileMetadata.GlobalTransitId.GetValueOrDefault());

            // var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
            // var file = new ExternalFileIdentifier()
            // {
            //     TargetDrive = e.targetDrive,
            //     FileId = e.header.FileId
            // };
            //
            // // var response = sam.DriveRedux.SendReadReceipt([file]).GetAwaiter().GetResult();
            // if (response.IsSuccessStatusCode)
            // {
            //     _readReceiptsSentBySam.Add(e.header.FileMetadata.GlobalTransitId.GetValueOrDefault());
            // }
        }


        private async Task Shutdown(OwnerApiClientRedux hostIdentity, OwnerApiClientRedux frodo, OwnerApiClientRedux sam)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(hostIdentity.Identity.OdinId, frodo.Identity.OdinId);
            await _scaffold.OldOwnerApi.DisconnectIdentities(hostIdentity.Identity.OdinId, sam.Identity.OdinId);

            await this._frodoSocketHandler.DisconnectAsync();
            await this._samSocketHandler.DisconnectAsync();
        }

        private async Task SendBarrage(OwnerApiClientRedux hostIdentity, OwnerApiClientRedux frodo, OwnerApiClientRedux sam,
            TargetDrive targetDrive,
            int maxThreads, int iterations)
        {
            async Task<(long bytesWritten, long[] measurements)> Func(int threadNumber, int count)
            {
                long[] timers = new long[count];
                var sw = new Stopwatch();

                for (int i = 0; i < count; i++)
                {
                    sw.Restart();

                    string message = "hi";
                    var result = await SendChatMessage(message, frodo, hostIdentity, targetDrive, true);
                    if (result!.RecipientStatus[hostIdentity.Identity.OdinId] == TransferStatus.Enqueued)
                    {
                        lock (_lock)
                        {
                            // _filesSentByFrodo.Add(result.GlobalTransitIdFileIdentifier.GlobalTransitId);
                            _filesSentByFrodo.Add(result.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);
                        }
                    }

                    timers[i] = sw.ElapsedMilliseconds;
                    // If you want to introduce a delay be sure to use: await Task.Delay(1);
                    await Task.Delay(300);
                }

                return (0, timers);
            }

            await PerformanceFramework.ThreadedTestAsync(maxThreads, iterations, Func);
        }

        private async Task WaitForEmptyOutboxes(OwnerApiClientRedux hostIdentity, OwnerApiClientRedux frodo, OwnerApiClientRedux sam,
            TargetDrive targetDrive,
            TimeSpan timeout)
        {
            var senderWaitTime = await frodo.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, timeout);
            Console.WriteLine($"Sender Outbox Wait time: {senderWaitTime.TotalSeconds}sec");
            //
            // var hostWaitTime = await hostIdentity.DriveRedux.WaitForEmptyOutbox(targetDrive, timeout);
            // Console.WriteLine($"Sender Outbox Wait time: {hostWaitTime.TotalSeconds}sec");
            //
            // var recipientWaitTime = await sam.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, timeout);
            // Console.WriteLine($"Sender Outbox Wait time: {recipientWaitTime.TotalSeconds}sec");
        }

        private async Task WaitForEmptyInboxes(OwnerApiClientRedux hostIdentity, OwnerApiClientRedux sender, OwnerApiClientRedux recipient,
            TargetDrive targetDrive,
            TimeSpan timeout)
        {
            var hostWaitTime = await hostIdentity.DriveRedux.WaitForEmptyInbox(targetDrive, timeout);
            Console.WriteLine($"Sender Inbox Wait time: {hostWaitTime.TotalSeconds}sec");

            // var senderWaitTime = await sender.DriveRedux.WaitForEmptyInbox(targetDrive, timeout);
            // Console.WriteLine($"Sender Inbox Wait time: {senderWaitTime.TotalSeconds}sec");

            // var recipientWaitTime = await recipient.DriveRedux.WaitForEmptyInbox(targetDrive, timeout);
            // Console.WriteLine($"Sender Inbox Wait time: {recipientWaitTime.TotalSeconds}sec");
        }

        private async Task<TransitResult> SendChatMessage(string message, OwnerApiClientRedux sender, OwnerApiClientRedux recipient,
            TargetDrive targetDrive,
            bool allowDistribution)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = allowDistribution,
                IsEncrypted = true,
                AppData = new()
                {
                    Content = message,
                    FileType = default,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.Connected
            };

            var response = await sender.PeerDirect.TransferMetadata(
                targetDrive,
                fileMetadata,
                [recipient.Identity.OdinId],
                null
            );

            return response.Content;
        }

        public async Task ValidateFileDelivered(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, ExternalFileIdentifier file)
        {
            // Assert: file that was sent has peer transfer status updated
            var uploadedFileResponse1 = await sender.DriveRedux.GetFileHeader(file);
            Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content;

            Assert.IsTrue(
                uploadedFile1.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipient.Identity.OdinId, out var recipientStatus));
            Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            Assert.IsFalse(recipientStatus.IsInOutbox);
            Assert.IsFalse(recipientStatus.IsReadByRecipient);
            Assert.IsFalse(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
            // Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == targetVersionTag);
        }


        private async Task<(AppApiClientRedux frodoAppApi, AppApiClientRedux samAppApi)> PrepareScenario(OwnerApiClientRedux hostIdentity,
            OwnerApiClientRedux frodo,
            OwnerApiClientRedux sam,
            TargetDrive groupChannelDrive)
        {
            // TODO: change when we upgrade the drive to support settings a read-acl
            Dictionary<string, string> isCollaborativeChannel =
                new() { { FeedDriveDistributionRouter.IsCollaborativeChannel, bool.TrueString } };

            // Setup host identity with community-chat drive

            await hostIdentity.DriveManager.CreateDrive(groupChannelDrive, "A Group Channel Drive",
                "",
                allowAnonymousReads: false,
                ownerOnly: false,
                allowSubscriptions: false,
                attributes: isCollaborativeChannel);

            var memberCircleId = Guid.NewGuid();

            await hostIdentity.Network.CreateCircle(memberCircleId, "group members", new PermissionSetGrantRequest()
                {
                    Drives = new List<DriveGrantRequest>()
                    {
                        new()
                        {
                            PermissionedDrive = new()
                            {
                                Drive = groupChannelDrive,
                                Permission = DrivePermission.ReadWrite
                            },
                        }
                    },
                    PermissionSet = default
                }
            );

            // connect members to host; note they're not connected to each other

            await hostIdentity.Connections.SendConnectionRequest(frodo.Identity.OdinId, [memberCircleId]);
            await hostIdentity.Connections.SendConnectionRequest(sam.Identity.OdinId, [memberCircleId]);

            await frodo.Connections.AcceptConnectionRequest(hostIdentity.Identity.OdinId);
            await sam.Connections.AcceptConnectionRequest(hostIdentity.Identity.OdinId);

            // Create an app to receive the notifications

            Guid communityAppId = Guid.NewGuid();
            var permissions = new PermissionSetGrantRequest
            {
                Drives = [],
                PermissionSet = new PermissionSet(PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead)
            };

            var frodoAppClientAccessToken = await frodo.AppManager.RegisterAppAndClient(communityAppId, permissions);
            var samAppClientAccessToken = await sam.AppManager.RegisterAppAndClient(communityAppId, permissions);


            // now the app client is going to call to the identity to
            // request a CAT to receive app notifications from the peer identity
            var frodoAppApiClient = _scaffold.CreateAppApiClientRedux(frodo.Identity.OdinId, frodoAppClientAccessToken);
            var samAppApiClient = _scaffold.CreateAppApiClientRedux(sam.Identity.OdinId, samAppClientAccessToken);

            return (frodoAppApiClient, samAppApiClient);
        }
    }
}