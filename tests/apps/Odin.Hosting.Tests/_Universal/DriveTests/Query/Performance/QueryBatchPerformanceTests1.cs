using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Storage.Database;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.Performance;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests.Query.Performance
{
    public class QueryBatchPerformanceTests1
    {
        private WebScaffold _scaffold;

        private static readonly AsyncLock _lock = new();

        private readonly List<KeyValuePair<string, Guid>> _filesSent = new();

        private const int ProcessInboxBatchSize = 100000;

        private const int ChatMessageFileType = 887;
        private static Guid ChatMessageTag = Guid.Parse("5e698447-8f2e-46b6-b082-4bd1f7d96c64");

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            var fixedSubPath = "logme";
            _scaffold = new WebScaffold(folder, fixedSubPath);

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
        public async Task QueryBatchPerformance_ScenarioNoTag()
        {
            /*
             * Frodo and sam are chatting; they get into a heated debate and chat goes really fast
             * As they chat, items are sent out of the outbox to the recipient
             * As the recipient receives items, the recipient sends back a read-receipt; which also goes into the outbox
             * I need to ensure the outbox is being emptied and at the end of the test; no items remain (with in X minutes)
             */

            //
            // Setup
            //
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            await PrepareScenario(frodo, sam);
            await CreateConversation(frodo, sam);

            Console.WriteLine("Setup:");
            Console.WriteLine($"\tFrodo Sent Chats: {_filesSent.Count(kvp => kvp.Key == frodo.OdinId)}");
            Console.WriteLine($"\tSam Sent Chats: {_filesSent.Count(kvp => kvp.Key == sam.OdinId)}");

            //
            // Act
            //

            var qbr = new QueryBatchRequest
            {
                QueryParams = new()
                {
                    TargetDrive = SystemDriveConstants.ChatDrive,
                    FileType = [ChatMessageFileType],
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = 30
                }
            };

            SimplePerformanceCounter.Reset();

            var counters = _scaffold.Services.GetRequiredService<DatabaseCounters>();
            counters.Reset();

            await MeasureQueryBatch(frodo, qbr, maxThreads: 1, iterations: 5000);
            // await MeasureQueryBatch(sam, maxThreads: 5, iterations: 50);

            Console.WriteLine("Results:");
            Console.WriteLine(counters.ToString());
            Console.WriteLine(SimplePerformanceCounter.Dump());

            await Shutdown(frodo, sam);
        }


        [Test, Explicit]
        public async Task QueryBatchPerformance_ScenarioWithTag()
        {
            /*
             * Frodo and sam are chatting; they get into a heated debate and chat goes really fast
             * As they chat, items are sent out of the outbox to the recipient
             * As the recipient receives items, the recipient sends back a read-receipt; which also goes into the outbox
             * I need to ensure the outbox is being emptied and at the end of the test; no items remain (with in X minutes)
             */

            //
            // Setup
            //
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            await PrepareScenario(frodo, sam);
            await CreateConversation(frodo, sam);

            Console.WriteLine("Setup:");
            Console.WriteLine($"\tFrodo Sent Chats: {_filesSent.Count(kvp => kvp.Key == frodo.OdinId)}");
            Console.WriteLine($"\tSam Sent Chats: {_filesSent.Count(kvp => kvp.Key == sam.OdinId)}");

            //
            // Act
            //
            var qbr = new QueryBatchRequest
            {
                QueryParams = new()
                {
                    TargetDrive = SystemDriveConstants.ChatDrive,
                    FileType = [ChatMessageFileType],
                    TagsMatchAtLeastOne = [ChatMessageTag]
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = Int32.MaxValue
                }
            };

            var counters = _scaffold.Services.GetRequiredService<DatabaseCounters>();
            counters.Reset();
            await MeasureQueryBatch(frodo, qbr, maxThreads: 5, iterations: 50);
            // await MeasureQueryBatch(sam, maxThreads: 5, iterations: 50);

            Console.WriteLine("Results:");
            Console.WriteLine(counters.ToString());

            await Shutdown(frodo, sam);
        }


        private async Task Shutdown(OwnerApiClientRedux sender, OwnerApiClientRedux recipient)
        {
            await this.DeleteScenario(sender, recipient);
        }

        private async Task BuildConversation(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, int maxThreads, int iterations)
        {
            async Task Func(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    string message = "hi";
                    var result = await SendChatMessage(message, sender, recipient, true);
                    if (result!.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Enqueued)
                    {
                        using (await _lock.LockAsync())
                        {
                            _filesSent.Add(new KeyValuePair<string, Guid>(
                                sender.OdinId.DomainName,
                                result.GlobalTransitIdFileIdentifier.GlobalTransitId));
                        }
                    }
                }
            }

            await RunThreads(maxThreads, iterations, Func);
        }

        private async Task WaitForEmptyOutboxes(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TimeSpan timeout)
        {
            var senderWaitTime = await sender.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.ChatDrive, timeout);
            Console.WriteLine($"Sender Outbox Wait time: {senderWaitTime.TotalSeconds}sec");

            var recipientWaitTime = await recipient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.ChatDrive, timeout);
            Console.WriteLine($"Recipient Outbox Wait time: {recipientWaitTime.TotalSeconds}sec");
        }

        private async Task WaitForEmptyInboxes(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TimeSpan timeout)
        {
            _ = sender.DriveRedux.ProcessInbox(SystemDriveConstants.ChatDrive, ProcessInboxBatchSize);
            _ = recipient.DriveRedux.ProcessInbox(SystemDriveConstants.ChatDrive, ProcessInboxBatchSize);

            var senderWaitTime = await sender.DriveRedux.WaitForEmptyInbox(SystemDriveConstants.ChatDrive, timeout);
            Console.WriteLine($"Sender Inbox Wait time: {senderWaitTime.TotalSeconds}sec");

            var recipientWaitTime = await recipient.DriveRedux.WaitForEmptyInbox(SystemDriveConstants.ChatDrive, timeout);
            Console.WriteLine($"Recipient Inbox Wait time: {recipientWaitTime.TotalSeconds}sec");
        }

        private async Task<UploadResult> SendChatMessage(string message, OwnerApiClientRedux sender, OwnerApiClientRedux recipient,
            bool allowDistribution)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = allowDistribution,
                IsEncrypted = true,
                AppData = new()
                {
                    Content = message,
                    FileType = ChatMessageFileType,
                    GroupId = default,
                    Tags = [ChatMessageTag]
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

        private async Task MeasureQueryBatch(OwnerApiClientRedux identity, QueryBatchRequest qbr, int maxThreads, int iterations)
        {
            async Task<(long bytesWritten, long[] measurements)> Func(int threadNumber, int count)
            {
                long bw = 0;
                long[] timers = new long[count];
                var sw = new Stopwatch();

                for (int i = 0; i < count; i++)
                {
                    sw.Restart();

                    var response = await identity.DriveRedux.QueryBatch(qbr);
                    bw += response.ContentHeaders.ContentLength ?? 0;

                    Assert.IsTrue(response.IsSuccessStatusCode);
                    // var results = response.Content.SearchResults;
                    // response.SearchResults.Count();

                    timers[i] = sw.ElapsedMilliseconds;
                }

                return (bw, timers);
            }

            await PerformanceFramework.ThreadedTestAsync(maxThreads, iterations, Func);
        }

        private async Task CreateConversation(OwnerApiClientRedux frodo, OwnerApiClientRedux sam)
        {
            await BuildConversation(frodo, sam, maxThreads: 5, iterations: 100);

            // await BuildConversation(sam, frodo, maxThreads: 5, iterations: 50);

            await WaitForEmptyOutboxes(frodo, sam, TimeSpan.FromSeconds(60));

            await WaitForEmptyInboxes(frodo, sam, TimeSpan.FromSeconds(90));
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipient.Identity.OdinId);
        }

        private static async Task RunThreads(int maxThreads, int iterations, Func<int, Task> functionToExecute)
        {
            Task[] tasks = new Task[maxThreads];
            for (int i = 0; i < maxThreads; i++)
            {
                tasks[i] = Task.Run(async () => { await functionToExecute(iterations); });
            }

            await Task.WhenAll(tasks);
        }
    }
}