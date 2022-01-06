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
using Youverse.Core.Services.Transit.Inbox;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    public class InboxTests
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

        [TearDown]
        public void TearDown()
        {

        }


        // [Test(Description = "")]
        // public async Task CanOnlyGetInboxItemsForApp()
        // {
        //     // await SendTransfer();
        //     
        //     //TODO: add support to CreateHttpClient which allows us to choose a different AppId and deviceUid
        //     // so we can ensure no cross calling; unless allowed
        //     // using (var client = _scaffold.CreateHttpClient(DotYouIdentities.Samwise, false, true))
        //     // {
        //     // }
        // }

        [Test(Description = "")]
        public async Task CanGetInboxList()
        {
            await SendTransfer();
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<ITransitInboxHttpClient>(client);
                var itemsResponse = await svc.GetInboxItems(1, 100);
            
                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count > 0); //TODO: need to actually check for an accurate count
            }
        }
        
        [Test(Description = "")]
        public async Task CanRemoveInboxItem()
        {
            await SendTransfer();
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<ITransitInboxHttpClient>(client);
                var itemsResponse = await svc.GetInboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                var itemId = items.Results.First().Id;
                var removeItemResponse = await svc.RemoveInboxItem(itemId);
                Assert.IsTrue(removeItemResponse.IsSuccessStatusCode);

                var getItemResponse = await svc.GetInboxItem(itemId);

                Assert.IsTrue(getItemResponse.IsSuccessStatusCode);
                Assert.IsTrue(getItemResponse.Content == null);
            }
        }

        [Test(Description = "")]
        public async Task CanGetInboxItem()
        {
            await SendTransfer();
            using (var client = _scaffold.CreateOwnerApiHttpClient( TestIdentities.Frodo))
            {
                var svc = RestService.For<ITransitInboxHttpClient>(client);
                var itemsResponse = await svc.GetInboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count == 1);

                var singleItemResponse = await svc.GetInboxItem(items.Results.First().Id);

                Assert.IsTrue(singleItemResponse.IsSuccessStatusCode);
                var singleItem = singleItemResponse.Content;
                Assert.IsNotNull(singleItem);
                Assert.IsTrue(singleItem.Id == items.Results.First().Id);
            }
        }

        private async Task SendTransfer()
        {
            var sender = TestIdentities.Samwise;
            var recipient = TestIdentities.Frodo;
            
            var appSharedSecret = new SensitiveByteArray(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
            var transferIv = ByteArrayUtil.GetRndByteArray(16);

            var keyHeader = new KeyHeader()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                AesKey = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16))
            };

            var metadataJson = "{metadata:true, message:'pie on sky}";
            var metaDataCipher = Utils.GetEncryptedStream(metadataJson, keyHeader);

            var payloadJson = "{payload:true, image:'b64 data'}";
            var payloadCipher = Utils.GetEncryptedStream(payloadJson, keyHeader);

            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, appSharedSecret.GetKey());

            var b = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ekh));
            var encryptedKeyHeaderStream = new MemoryStream(b);

            var recipientList = new RecipientList { Recipients = new List<DotYouIdentity>() { recipient } };
            var recipientJson = JsonConvert.SerializeObject(recipientList);

            var recipientCipher = Utils.EncryptAes(recipientJson, transferIv, appSharedSecret.GetKey());

            keyHeader.AesKey.Wipe();
            appSharedSecret.Wipe();
            
            using (var client = _scaffold.CreateOwnerApiHttpClient(sender))
            {
                var transitSvc = RestService.For<ITransitHttpClient>(client);

                
                var response = await transitSvc.SendFile(
                    new StreamPart(encryptedKeyHeaderStream, "tekh.encrypted", "application/json", "tekh"),
                    new StreamPart(recipientCipher, "recipientlist.encrypted", "application/json", "recipients"),
                    new StreamPart(metaDataCipher, "metadata.encrypted", "application/json", "metadata"),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", "payload"));

                Assert.IsTrue(response.IsSuccessStatusCode);
                var transferResult = response.Content;
                Assert.IsNotNull(transferResult);
                
                Assert.IsNotNull(transferResult.File, "File was not set");
                Assert.IsFalse(transferResult.File.FileId== Guid.Empty, "FileId was not set");
                Assert.IsFalse(transferResult.File.DriveId == Guid.Empty, "DriveId was not set");
                Assert.IsTrue(transferResult.RecipientStatus.Count == 1, "Too many recipient results returned");
                Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), "Could not find matching recipient");
                Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated);

                await transitSvc.ProcessOutbox();
            }
            
            //System.Threading.Thread.Sleep(10 * 1000);
            
            
        }
    }
}