using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;

namespace Youverse.Hosting.Tests.DriveApi.App
{
    public class TransferFileTests
    {
        private TestScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new TestScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test(Description = "Test basic transfer")]
        public async Task CanSendTransferAndSeeStatus()
        {
            var sender = TestIdentities.Frodo;
            var recipients = new List<string>() { TestIdentities.Samwise };

            Guid appId = Guid.NewGuid();
            var testContext = await _scaffold.SetupTestSampleApp(appId, sender, false, TargetDrive.NewTargetDrive());

            var recipientContexts = new Dictionary<DotYouIdentity, TestSampleAppContext>();
            foreach (var r in recipients)
            {
                var recipient = (DotYouIdentity)r;
                var ctx = await _scaffold.SetupTestSampleApp(testContext.AppId, recipient, false, testContext.TargetDrive);
                recipientContexts.Add(recipient, ctx);

                await _scaffold.CreateConnection(sender, recipient);
            }

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = testContext.TargetDrive,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = recipients
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = testContext.AppSharedSecretKey.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    AppData = new()
                    {
                        Tags = new List<Guid>() { Guid.NewGuid() },
                        ContentIsComplete = true,
                        JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" })
                    }
                },
            };

            var fileDescriptorCipher = Utils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

            using (var client = _scaffold.CreateAppApiHttpClient(sender, testContext.ClientAuthenticationToken))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(transferResult.File.TargetDrive.IsValid());

                foreach (var recipient in instructionSet.TransitOptions.Recipients)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                    Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                }
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();

            foreach (var recipient in recipientContexts.Keys)
            {
                await _scaffold.DisconnectIdentities(sender, recipient);
            }


            //connect to all recipients to determine if they received

            // Now connect as frodo to see if he has a recent transfer from sam matching the file contents
            // using (var client = _scaffold.CreateHttpClient(DotYouIdentities.Frodo, true))
            // {
            //     //TODO: query for the message to see if 
            //
            //     var expectedMessage = sentMessage;
            //
            //     //Check audit 
            //     var transitSvc = RestService.For<ITransitTestHttpClient>(client);
            //     var recentAuditResponse = await transitSvc.GetRecentAuditEntries(5, 1, 100);
            //
            //     Assert.IsTrue(recentAuditResponse.IsSuccessStatusCode);
            //     var entry = recentAuditResponse.Content?.Results.FirstOrDefault(entry => entry.EventId == (int) TransitAuditEvent.Accepted);
            //     Assert.IsNotNull(entry, "Could not find audit event marked as Accepted");
            //
            //     //I guess I need an api that says give me all transfers from a given DI
            //     // so in this case I could ge that transfer and compare the file contents?
            //     //this api is needed for everything - so yea. let's do that
            // }


            //TODO: determine if we should check outgoing audit to show it was sent
            // var recentAuditResponse = await transitSvc.GetRecentAuditEntries(60, 1, 100);
            // Assert.IsTrue(recentAuditResponse.IsSuccessStatusCode);


            /*
             *so i think in a production scenario we will hve signalr sending a notification for a given app that a transfer has been received
             * but in the case when you're not online.. and sign in.. the signalr notification won't due because it's an 'online thing only'
             * so i thin it makes sense to have an api call which allows the recipient to query all incoming transfers that have not been processed
             */
        }

        // [Test(Description = "")]
        // public async Task TestCanRecoverFromRecipientExpiredPublic()
        // {
        // }
        //
        // [Test(Description = "")]
        // public async Task TestCanRecoverFromRecipientNotConnected()
        // {
        // }
        //
        // [Test(Description = "")]
        // public async Task TestCanRecoverFromRecipientServerDown()
        // {
        // }

        [Test(Description = "")]
        public async Task RecipientCanGetReceivedTransferFromDriveAndIsSearchable()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() { TestIdentities.Frodo };
            var categoryId = Guid.NewGuid();
            var message = "ping ping pong pong";
            var jsonMessage = JsonConvert.SerializeObject(new { message = message });
            var payloadText = "lets alllll prraayyy for this world";

            var utilsContext = await _scaffold.TransferFile(sender, recipients, new TransitTestUtilsOptions()
            {
                ProcessOutbox = true,
                ProcessTransitBox = true,
                AppDataCategoryId = categoryId,
                AppDataJsonContent = jsonMessage,
                PayloadData = payloadText
            });

            var recipientContext = utilsContext.RecipientContexts[TestIdentities.Frodo];
            using (var recipientClient = _scaffold.CreateAppApiHttpClient(TestIdentities.Frodo, recipientContext.ClientAuthenticationToken))
            {
                var svc = RestService.For<ITransitTestAppHttpClient>(recipientClient);
                var itemsResponse = await svc.GetInboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count == 1);

                var inboxItemResponse = await svc.GetInboxItem(items.Results.First().Id);

                Assert.IsTrue(inboxItemResponse.IsSuccessStatusCode);
                var inboxItem = inboxItemResponse.Content;
                Assert.IsNotNull(inboxItem);
                Assert.IsTrue(inboxItem.Id == items.Results.First().Id);

                var driveSvc = RestService.For<IDriveTestHttpClientForApps>(recipientClient);

                var fileHeaderResponse = await driveSvc.GetFileHeader(inboxItem.File.TargetDrive, inboxItem.File.FileId);
                Assert.That(fileHeaderResponse.IsSuccessStatusCode, Is.True);
                Assert.That(fileHeaderResponse.Content, Is.Not.Null);

                var clientFileHeader = fileHeaderResponse.Content;

                Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

                Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(utilsContext.FileMetadata.ContentType));
                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, utilsContext.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(utilsContext.FileMetadata.AppData.JsonContent));
                Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(utilsContext.FileMetadata.AppData.ContentIsComplete));

                Assert.That(clientFileHeader.EncryptedKeyHeader, Is.Not.Null);
                Assert.That(clientFileHeader.EncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(clientFileHeader.EncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(clientFileHeader.EncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv byte array was all zeros");
                Assert.That(clientFileHeader.EncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var key = recipientContext.AppSharedSecretKey.ToSensitiveByteArray();
                var decryptedKeyHeader = clientFileHeader.EncryptedKeyHeader.DecryptAesToKeyHeader(ref key);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                var fileKey = decryptedKeyHeader.AesKey;
                Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

                //get the payload and decrypt, then compare
                var payloadResponse = await driveSvc.GetPayload(inboxItem.File.TargetDrive, inboxItem.File.FileId);
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                var bytes = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    Key: ref bytes,
                    IV: decryptedKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadText);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                var driveQueryClient = RestService.For<IDriveTestHttpClientForApps>(recipientClient);

                var startCursor = Array.Empty<byte>();
                var stopCursor = Array.Empty<byte>();
                var qp = new QueryParams()
                {
                    TagsMatchAtLeastOne = new List<byte[]>() { categoryId.ToByteArray() }
                };

                var resultOptions = new ResultOptions()
                {
                    IncludeMetadataHeader = true,
                    MaxRecords = 100
                };

                var response = await driveQueryClient.GetBatch(recipientContext.TargetDrive, startCursor, stopCursor, qp, resultOptions);
                
//                var response = await driveQueryClient.GetBatch(recipientContext.DriveAlias, categoryId, true, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);

                Assert.IsTrue(batch.SearchResults.Count() == 1);
                CollectionAssert.AreEquivalent(utilsContext.FileMetadata.AppData.Tags, batch.SearchResults.First().Tags);


                // Console.WriteLine($"Items with category: {categoryId}");
                // foreach (var item in page.Results)
                // {
                //     Console.WriteLine($"{item.PrimaryCategoryId} {item.JsonContent}");
                // }
            }
        }
    }
}