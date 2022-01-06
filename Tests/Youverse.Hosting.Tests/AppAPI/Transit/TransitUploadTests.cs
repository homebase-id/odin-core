using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    public class TransitUploadTests
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

        [Test(Description = "Test Upload only; no expire, no drive; no transfer")]
        public async Task UploadOnly()
        {
            var identity = TestIdentities.Frodo;
            var (appId, deviceUid, authResult, appSharedSecretKey) = await _scaffold.SetupSampleApp(identity);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                
                StorageOptions = new StorageOptions()
                {
                    DriveId = null,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },
                
                TransitOptions = new TransitOptions()
                {
                    RecipientsList = null
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, appSharedSecretKey),
                FileMetadata = new UploadFileMetadata()
                {
                    ContentType = "application/json",
                    AppData = new AppFileMetaData()
                    {
                        CategoryId = Guid.Empty,
                        ContentIsComplete = true,
                        JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" })
                    }
                },
            };

            var fileDescriptorCipher = Utils.JsonEncryptAes(descriptor, transferIv, appSharedSecretKey);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

            using (var client = _scaffold.CreateAppApiHttpClient(identity, authResult))
            {
                var transitSvc = RestService.For<ITransitHttpClient>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", "instructionSet"),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", "fileDescriptor"),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", "payload"));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.DriveId, Is.Not.EqualTo(Guid.Empty));

                Assert.That(transferResult.RecipientStatus, Is.Not.Null);
                Assert.IsTrue(transferResult.RecipientStatus.Count == 0, "Too many recipient results returned");
            }

            keyHeader.AesKey.Wipe();
        }

        [Test(Description = "Test basic transfer")]
        public async Task TestBasicTransfer()
        {
            var appSharedSecret = new SensitiveByteArray(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            //TODO: need to update api to accept a driveId
            var file = new DriveFileId()
            {
                DriveId = Guid.Empty,
                FileId = Guid.Empty
            };

            var metadata = new FileMetadata(file)
            {
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ContentType = "application/json",
                AppData = new AppFileMetaData()
                {
                    CategoryId = Guid.Empty,
                    ContentIsComplete = true,
                    JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" })
                }
            };

            var metadataJson = JsonConvert.SerializeObject(metadata);
            var metaDataCipher = Utils.EncryptAes(metadataJson, transferIv, appSharedSecret.GetKey());

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, appSharedSecret.GetKey());
            var b = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ekh));
            var encryptedKeyHeaderStream = new MemoryStream(b);

            var recipientList = new RecipientList { Recipients = new List<DotYouIdentity>() { TestIdentities.Frodo } };
            var recipientJson = JsonConvert.SerializeObject(recipientList);
            var recipientCipher = Utils.EncryptAes(recipientJson, transferIv, appSharedSecret.GetKey());

            keyHeader.AesKey.Wipe();
            appSharedSecret.Wipe();

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                //sam to send frodo a data transfer, small enough to send it instantly

                var transitSvc = RestService.For<ITransitHttpClient>(client);

                var response = await transitSvc.SendFile(
                    new StreamPart(encryptedKeyHeaderStream, "tekh.encrypted", "application/json", "tekh"),
                    new StreamPart(recipientCipher, "recipientlist.encrypted", "application/json", "recipients"),
                    new StreamPart(metaDataCipher, "metadata.encrypted", "application/json", "metadata"),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", "payload"));

                Assert.IsTrue(response.IsSuccessStatusCode);
                var transferResult = response.Content;
                Assert.IsNotNull(transferResult);
                Assert.IsFalse(transferResult.FileId == Guid.Empty, "FileId was not set");
                Assert.IsTrue(transferResult.RecipientStatus.Count == 1, "Too many recipient results returned");
                Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(TestIdentities.Frodo), "Could not find matching recipient");
                Assert.IsTrue(transferResult.RecipientStatus[TestIdentities.Frodo] == TransferStatus.TransferKeyCreated);

                //there should be a record in the outbox for this transfer
                var outboxItemsResponse = await transitSvc.GetOutboxItems(1, 100);
                Assert.IsTrue(outboxItemsResponse.IsSuccessStatusCode);

                //In this test framework, we sent one item so there
                //should be one item in the outbox.  Be sure the outbox
                //processor is not enabled
                var outboxItems = outboxItemsResponse.Content;
                Assert.IsNotNull(outboxItems);
                Assert.IsTrue(outboxItems.Results.Count == 1);

                var item = outboxItems.Results.First();
                Assert.IsTrue(item.Recipient == TestIdentities.Frodo);
                // Assert.IsTrue(item.DeviceUid == _scaffold.DeviceUid);

                //TODO: How do i check the transfer key was populated?  Note: will leave this out and have it tested by ensuring the message is received and can be decrypted by the receipient
                //TODO: how do i check Pending Transfer Queue?

                await transitSvc.ProcessOutbox();
            }

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
    }
}