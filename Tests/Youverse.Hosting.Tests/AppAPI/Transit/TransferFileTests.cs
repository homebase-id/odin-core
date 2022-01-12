using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Tests.AppAPI.Drive;

namespace Youverse.Hosting.Tests.AppAPI.Transit
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
        public async Task TestBasicTransfer()
        {
            var sender = TestIdentities.Frodo;
            var recipients = new List<string>() {TestIdentities.Samwise};

            var testContext = await _scaffold.SetupTestSampleApp(sender);

            var recipientContexts = new Dictionary<DotYouIdentity, TestSampleAppContext>();
            foreach (var r in recipients)
            {
                var recipient = (DotYouIdentity) r;
                var ctx = await _scaffold.SetupTestSampleApp(testContext.AppId, recipient);
                recipientContexts.Add(recipient, ctx);
            }

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
                    Recipients = recipients
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, testContext.AppSharedSecretKey),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    AppData = new()
                    {
                        CategoryId = Guid.Empty,
                        ContentIsComplete = true,
                        JsonContent = JsonConvert.SerializeObject(new {message = "We're going to the beach; this is encrypted by the app"})
                    }
                },
            };

            var fileDescriptorCipher = Utils.JsonEncryptAes(descriptor, transferIv, testContext.AppSharedSecretKey);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

            using (var client = _scaffold.CreateAppApiHttpClient(sender, testContext.AuthResult))
            {
                var transitSvc = RestService.For<ITransitTestHttpClient>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.DriveId, Is.Not.EqualTo(Guid.Empty));

                foreach (var recipient in instructionSet.TransitOptions.Recipients)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                    Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                }
            }

            keyHeader.AesKey.Wipe();

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
    }
}