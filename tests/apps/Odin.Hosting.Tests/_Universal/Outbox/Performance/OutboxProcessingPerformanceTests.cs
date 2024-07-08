using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter.Xml;
using Microsoft.AspNetCore.Connections.Features;
using Nito.AsyncEx;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.Performance;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Dns.PowerDns;
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
        private readonly List<Guid> _filesSentByFrodo = new();

        private readonly List<Guid> _readReceiptsReceivedByFrodo = new();
        private readonly List<Guid> _filesReceivedBySam = new();
        private readonly List<Guid> _readReceiptsSentBySam = new();

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
        public async Task ChatSpamTestEndToEnd_AllSuccessScenarios()
        {
            /*
             * Frodo and sam are chatting; they get into a heated debate and chat goes really fast
             * As they chat, items are sent out of the outbox to the recipient
             * As the recipient receives items, the recipient sends back a read-receipt; which also goes into the outbox
             * I need to ensure the outbox is being emptied and at the end of the test; no items remain (with in X minutes)
             */

            // Setup
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            await PrepareScenario(frodo, sam);
            await SetupSockets(frodo, sam);

            // Act
            await SendBarrage(frodo, sam);

            //
            // Wait for outbox to empty
            //
            // await WaitForEmptyOutboxes(TimeSpan.FromSeconds(30), frodo, sam);
            await frodo.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.ChatDrive);
            await sam.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.ChatDrive);

            Console.WriteLine($"Sent Files: {_filesSentByFrodo.Count}");
            Console.WriteLine($"Received Files:{_filesReceivedBySam.Count}");
            Console.WriteLine($"Read-receipts Sent: {_readReceiptsSentBySam.Count}");
            Console.WriteLine($"Read-receipts received: {_readReceiptsReceivedByFrodo.Count}");

            CollectionAssert.AreEquivalent(_filesSentByFrodo, _filesReceivedBySam);
            CollectionAssert.AreEquivalent(_filesReceivedBySam, _readReceiptsSentBySam,
                "mismatch in number of read-receipts send by sam to the files received");
            
            CollectionAssert.AreEquivalent(_readReceiptsSentBySam, _readReceiptsReceivedByFrodo);
            

            // Console.WriteLine($"SentFiles: {_sentFiles.Count}\t Modified (frodo) Files:{_filesModifiedOnFrodo.Count}");
            // CollectionAssert.AreEquivalent(_sentFiles, _filesModifiedOnFrodo);

            await Shutdown(frodo, sam);
        }

        private async Task SetupSockets(OwnerApiClientRedux frodo, OwnerApiClientRedux sam)
        {
            await _samSocketHandler.ConnectAsync(sam);
            _samSocketHandler.FileAdded += SamSocketHandlerOnFileAdded;

            await _frodoSocketHandler.ConnectAsync(frodo);
            _frodoSocketHandler.FileModified += FrodoSocketHandlerOnFileModified;
        }

        private void FrodoSocketHandlerOnFileModified(object sender, (TargetDrive targetDrive, SharedSecretEncryptedFileHeader header) e)
        {
            //validate sam marked ita s ready
            if (e.header.ServerMetadata.TransferHistory.Recipients.TryGetValue(TestIdentities.Samwise.OdinId, out var value))
            {
                if (value.IsReadByRecipient)
                {
                    _readReceiptsReceivedByFrodo.Add(e.header.FileMetadata.GlobalTransitId.GetValueOrDefault());
                }
            }
        }

        private void SamSocketHandlerOnFileAdded(object sender, (TargetDrive targetDrive, SharedSecretEncryptedFileHeader header) e)
        {
            _filesReceivedBySam.Add(e.header.FileMetadata.GlobalTransitId.GetValueOrDefault());

            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
            var file = new ExternalFileIdentifier()
            {
                TargetDrive = e.targetDrive,
                FileId = e.header.FileId
            };

            var response = sam.DriveRedux.SendReadReceipt([file]).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                _readReceiptsSentBySam.Add(e.header.FileMetadata.GlobalTransitId.GetValueOrDefault());
            }
        }


        private async Task Shutdown(OwnerApiClientRedux sender, OwnerApiClientRedux recipient)
        {
            await this.DeleteScenario(sender, recipient);
            await this._frodoSocketHandler.DisconnectAsync();
            await this._samSocketHandler.DisconnectAsync();
        }

        private async Task SendBarrage(OwnerApiClientRedux sender, OwnerApiClientRedux recipient)
        {
            async Task<(long bytesWritten, long[] measurements)> Func(int threadNumber, int iterations)
            {
                long[] timers = new long[iterations];
                var sw = new Stopwatch();

                for (int count = 0; count < iterations; count++)
                {
                    sw.Restart();

                    string message = "hi";
                    // var bytes = message.ToUtf8ByteArray().Length;
                    var result = await SendChatMessage(message, sender, recipient);
                    if (result!.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Enqueued)
                    {
                        _filesSentByFrodo.Add(result.GlobalTransitIdFileIdentifier.GlobalTransitId);
                    }

                    timers[count] = sw.ElapsedMilliseconds;
                    // If you want to introduce a delay be sure to use: await Task.Delay(1);
                    await Task.Delay(100);
                }

                return (0, timers);
            }

            await PerformanceFramework.ThreadedTestAsync(maxThreads: 2, iterations: 20, Func);
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


        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipient)
        {
            await senderOwnerClient.Connections.SendConnectionRequest(recipient.Identity.OdinId, []);

            //
            // Recipient accepts; grants access to circle
            //
            await recipient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, []);

            var getConnectionInfoResponse = await recipient.Network.GetConnectionInfo(senderOwnerClient.Identity.OdinId);
            Assert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipient.Identity.OdinId);
        }
    }
}