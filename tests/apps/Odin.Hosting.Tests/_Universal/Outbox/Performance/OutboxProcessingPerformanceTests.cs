using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.Performance;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Outbox.Performance
{
    public class OutboxProcessingPerformanceTests
    {
        private WebScaffold _scaffold;

        private int _fileSendAttempts = 0;
        private readonly object _sentFilesLock = new();
        private readonly List<GlobalTransitIdFileIdentifier> _sentFiles = new();

        private readonly ReadReceiptSocketHandler _frodoSocketHandler = new();
        private readonly ReadReceiptSocketHandler _samSocketHandler = new();

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


        [Test]
        public async Task AnUncertainTest()
        {
            /*
             * Frodo and sam are chatting; they get into a heated debate and chat goes really fast
             * As they chat, items are sent out of the outbox to the recipient
             * As the recipient receives items, the recipient sends back a read-receipt; which also goes into the outbox
             * I need to ensure the outbox is being emptied and at the end of the test; no items remain (with in X minutes)
             */

            // Setup
            // frodo and sam are connected; target drive is SystemDriveConstants.ChatDrive

            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            await PrepareScenario(frodo, [sam]);
            
            await _frodoSocketHandler.ConnectAsync(frodo);
            await _samSocketHandler.ConnectAsync(sam);

            // Act
            await SendBarrage(frodo, sam);

            // process the inbox when messages come in
            // await ProcessInbox(sam);

            //
            // Wait for outbox to empty
            //
            await WaitForEmptyOutboxes(TimeSpan.FromSeconds(30), frodo, sam);

            await this.DeleteScenario(frodo, [sam]);
            await this._frodoSocketHandler.DisconnectAsync();
            await this._samSocketHandler.DisconnectAsync();
        }

        private async Task SendBarrage(OwnerApiClientRedux sender, OwnerApiClientRedux recipient)
        {
            async Task<(long bytesWritten, long[] measurements)> Func(int threadNumber, int iterations)
            {
                long[] timers = new long[iterations];
                var result = await SendChatMessage("hi", sender, recipient);
                if (result!.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Enqueued)
                {
                    _sentFiles.Add(result.GlobalTransitIdFileIdentifier);
                }

                // timers[count] = sw.ElapsedMilliseconds;

                return (0, timers);
            }

            await PerformanceFramework.ThreadedTestAsync(maxThreads: 1, iterations: 10, Func);
        }

        private async Task WaitForEmptyOutboxes(TimeSpan timeout, params OwnerApiClientRedux[] clients)
        {
            var tasks = clients.Select(c => c.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.ChatDrive, timeout));
            await tasks.WhenAll();
        }

        private async Task<UploadResult> SendChatMessage(string message, OwnerApiClientRedux sender, OwnerApiClientRedux recipient)
        {
            _fileSendAttempts++;

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
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

            var storageOptions = new StorageOptions()
            {
                Drive = SystemDriveConstants.ChatDrive
            };

            var transitOptions = new TransitOptions()
            {
                Recipients = [recipient.Identity.OdinId]
            };

            var (uploadResponse, _) = await sender.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            return uploadResponse.Content;
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


        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, List<OwnerApiClientRedux> recipients)
        {
            foreach (var recipient in recipients)
            {
                //
                // Sender sends connection request
                //
                await senderOwnerClient.Connections.SendConnectionRequest(recipient.Identity.OdinId, []);
                await SetupRecipient(recipient, senderOwnerClient.Identity.OdinId);
            }
        }

        private static async Task SetupRecipient(OwnerApiClientRedux recipient, OdinId sender)
        {
            //
            // Recipient accepts; grants access to circle
            //
            await recipient.Connections.AcceptConnectionRequest(sender, []);

            // 
            // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var getConnectionInfoResponse = await recipient.Network.GetConnectionInfo(sender);

            Assert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
            var senderConnectionInfo = getConnectionInfoResponse.Content;
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, List<OwnerApiClientRedux> recipients)
        {
            foreach (var recipient in recipients)
            {
                await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipient.Identity.OdinId);
            }
        }
    }
}