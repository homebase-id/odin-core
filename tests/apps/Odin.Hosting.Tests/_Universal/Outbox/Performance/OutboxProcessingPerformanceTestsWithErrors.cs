using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.Performance;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Outbox.Performance
{
    public class OutboxProcessingPerformanceTestsWithErrors
    {
        private WebScaffold _scaffold;

        private static readonly object _lock = new();

        private readonly List<Guid> _failedFilesSentByFrodo = new();
        private readonly List<Guid> _filesSentByFrodo = new();

        private readonly List<Guid> _readReceiptsReceivedByFrodo = new();
        private readonly List<Guid> _filesReceivedBySam = new();
        private readonly List<Guid> _readReceiptsSentBySam = new();

        private const int ProcessInboxBatchSize = 10;
        private const int NotificationBatchSize = 10;
        private const int NotificationWaitTime = 10;

        private readonly ReadReceiptSocketHandler _frodoSocketHandler = new(ProcessInboxBatchSize, NotificationBatchSize, NotificationWaitTime);
        private readonly ReadReceiptSocketHandler _samSocketHandler = new(ProcessInboxBatchSize, NotificationBatchSize, NotificationWaitTime);

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
        [Description("Tests that the job manager will kick in if items in the inbox fail to be sent")]
        public async Task ChatSpamTestEndToEnd_AllowDistributeChangesToFalseDuringTest()
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

            const int errors = 2;
            const int maxThreads = 1;
            // Act
            await SendBarrageWithAllowDistributionErrors(frodo, sam, maxThreads, iterations: 2, errors);

            await WaitForEmptyOutboxes(frodo, sam, TimeSpan.FromSeconds(60));

            Console.WriteLine("Parameters:");

            Console.WriteLine("\tApp Notifications:");
            Console.WriteLine($"\t\tBatch Size: {NotificationBatchSize}");
            Console.WriteLine($"\t\tWait Time (ms): {NotificationWaitTime}");

            Console.WriteLine("\tInbox:");
            Console.WriteLine($"\t\tProcess Batch Size: {ProcessInboxBatchSize}");

            Console.WriteLine("Test Metrics:");
            Console.WriteLine($"\tSent Files: {_filesSentByFrodo.Count}");
            Console.WriteLine($"\tSent File (failures): {_failedFilesSentByFrodo.Count}");
            Console.WriteLine($"\tReceived Files:{_filesReceivedBySam.Count}");
            Console.WriteLine($"\tRead-receipts Sent: {_readReceiptsSentBySam.Count}");
            Console.WriteLine($"\tRead-receipts received: {_readReceiptsReceivedByFrodo.Count}");

            PerformanceCounter.WriteCounters();

            CollectionAssert.AreEquivalent(_filesSentByFrodo, _filesReceivedBySam);
            CollectionAssert.AreEquivalent(_filesReceivedBySam, _readReceiptsSentBySam,
                "mismatch in number of read-receipts send by sam to the files received");

            CollectionAssert.AreEquivalent(_readReceiptsSentBySam, _readReceiptsReceivedByFrodo);

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
                if (value.LatestTransferStatus == LatestTransferStatus.Delivered && !value.IsReadByRecipient)
                {
                    _filesSentByFrodo.Add(e.header.FileMetadata.GlobalTransitId.GetValueOrDefault());
                }

                if (value.LatestTransferStatus == LatestTransferStatus.SourceFileDoesNotAllowDistribution && !value.IsReadByRecipient)
                {
                    _failedFilesSentByFrodo.Add(e.header.FileMetadata.GlobalTransitId.GetValueOrDefault());
                }

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

        private async Task SendBarrageWithAllowDistributionErrors(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, int maxThreads, int iterations,
            int errors)
        {
            var randomErrorIntervals = GetRandomNumbers(0, iterations, errors);

            async Task<(long bytesWritten, long[] measurements)> Func(int threadNumber, int count)
            {
                long[] timers = new long[count];
                var sw = new Stopwatch();

                for (int i = 0; i < count; i++)
                {
                    sw.Restart();


                    string message = "hi";
                    var disableDistribution = randomErrorIntervals.Contains(i);
                    var result = await SendChatMessage(message, sender, recipient, allowDistribution: !disableDistribution);
                    // var bytes = message.ToUtf8ByteArray().Length;

                    if (result!.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Enqueued)
                    {
                        lock (_lock)
                        {
                            // _filesSentByFrodo.Add(result.GlobalTransitIdFileIdentifier.GlobalTransitId);
                        }
                    }

                    timers[i] = sw.ElapsedMilliseconds;
                    // If you want to introduce a delay be sure to use: await Task.Delay(1);
                    await Task.Delay(100);
                }

                return (0, timers);
            }

            await PerformanceFramework.ThreadedTestAsync(maxThreads, iterations, Func);
        }

        private List<int> GetRandomNumbers(int start, int end, int count)
        {
            if (count > (end - start + 1))
            {
                throw new ArgumentException("Count is greater than the range of numbers");
            }

            Random random = new Random();
            HashSet<int> randomNumbers = new HashSet<int>();

            while (randomNumbers.Count < count)
            {
                int number = random.Next(start, end + 1);
                randomNumbers.Add(number);
            }

            return [..randomNumbers];
        }

        private async Task WaitForEmptyOutboxes(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TimeSpan timeout)
        {
            var senderWaitTime = await sender.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.ChatDrive, timeout);
            Console.WriteLine($"Sender Outbox Wait time: {senderWaitTime.TotalSeconds}sec");

            var recipientWaitTime = await recipient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.ChatDrive, timeout);
            Console.WriteLine($"Sender Outbox Wait time: {recipientWaitTime.TotalSeconds}sec");
        }

        private async Task<UploadResult> SendChatMessage(string message, OwnerApiClientRedux sender, OwnerApiClientRedux recipient, bool allowDistribution)
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