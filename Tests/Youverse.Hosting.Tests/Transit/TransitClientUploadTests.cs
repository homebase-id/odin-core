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
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.Transit
{
    public class TransitClientUploadTests
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


        public Stream GetEncryptedStream(string data, KeyHeader keyHeader)
        {
            var cipher = Core.Cryptography.Crypto.AesCbc.EncryptBytesToBytes_Aes(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: keyHeader.AesKey.GetKey(),
                iv: keyHeader.Iv);

            return new MemoryStream(cipher);
        }

        public Stream GetAppSharedSecretEncryptedStream(string data, byte[] iv, byte[] key)
        {
            var cipher = Core.Cryptography.Crypto.AesCbc.EncryptBytesToBytes_Aes(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: key,
                iv: iv);

            return new MemoryStream(cipher);
        }


        [Test(Description = "Test basic transfer")]
        public async Task TestBasicTransfer()
        {
            var appSharedSecret = new SecureKey(Guid.Parse("4fc5b0fd-e21e-427d-961b-a2c7a18f18c5").ToByteArray());

            var keyHeader = new KeyHeader()
            {
                Iv = Guid.Empty.ToByteArray(), //ByteArrayUtil.GetRndByteArray(16),
                AesKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16))
            };

            var metadataJson = "{metadata:true, message:'pie on sky}";
            var metaDataCipher = GetEncryptedStream(metadataJson, keyHeader);

            var payloadJson = "{payload:true, image:'b64 data'}";
            var payloadCipher = GetEncryptedStream(payloadJson, keyHeader);

            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, appSharedSecret.GetKey());

            var b = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ekh));
            var encryptedKeyHeaderStream = new MemoryStream(b);

            var recipientList = new RecipientList {Recipients = new List<DotYouIdentity>() {_scaffold.Frodo}};
            var recipientJson = JsonConvert.SerializeObject(recipientList);

            var recipientCipher = GetAppSharedSecretEncryptedStream(recipientJson, ekh.Iv, appSharedSecret.GetKey());

            keyHeader.AesKey.Wipe();
            appSharedSecret.Wipe();

            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise, false))
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
                Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(_scaffold.Frodo), "Could not find matching recipient");
                Assert.IsTrue(transferResult.RecipientStatus[_scaffold.Frodo] == TransferStatus.TransferKeyCreated);

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
                Assert.IsTrue(item.Recipient == _scaffold.Frodo);
                Assert.IsTrue(item.AppId == _scaffold.AppId);
                Assert.IsTrue(item.DeviceUid == _scaffold.DeviceUid);

                //TODO: How do i check the transfer key was populated?  Note: will leave this out and have it tested by ensuring the message is received and can be decrypted by the receipient
                //TODO: how do i check Pending Transfer Queue?

                //try to hold out for the background job to process
                System.Threading.Thread.Sleep(5 * 1000);

            }

            // Now connect as frodo to see if he has a recent transfer from sam matching the file contents
            // using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo, true))
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