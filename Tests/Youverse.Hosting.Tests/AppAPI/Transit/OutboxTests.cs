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

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    public class OutboxTests
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
        // public async Task CanMarkItemAsFailureAndIsCheckedIn()
        // {
        // }


        // [Test(Description = "")]
        // public async Task CanCheckoutItem()
        // {
        // }

        // [Test(Description = "")]
        // public async Task CanOnlyGetOutboxItemsForApp()
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
        public async Task CanGetOutboxList()
        {
            await SendTransfer();
            using (var client = _scaffold.CreateOwnerApiHttpClient(DotYouIdentities.Samwise))
            {
                var svc = RestService.For<ITransitHttpClient>(client);
                var itemsResponse = await svc.GetOutboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count > 0); //TODO: need to actually check for an accurate count
            }
        }

        [Test(Description = "")]
        public async Task CanRemoveOutboxItem()
        {
            await SendTransfer();
            using (var client = _scaffold.CreateOwnerApiHttpClient(DotYouIdentities.Samwise))
            {
                var svc = RestService.For<ITransitHttpClient>(client);
                var itemsResponse = await svc.GetOutboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                var itemId = items.Results.First().Id;
                var removeItemResponse = await svc.RemoveOutboxItem(itemId);
                Assert.IsTrue(removeItemResponse.IsSuccessStatusCode);

                var getItemResponse = await svc.GetOutboxItem(itemId);

                Assert.IsTrue(getItemResponse.IsSuccessStatusCode);
                Assert.IsTrue(getItemResponse.Content == null);
            }
        }

        [Test(Description = "")]
        public async Task CanGetOutboxItem()
        {
            await SendTransfer();
            using (var client = _scaffold.CreateOwnerApiHttpClient(DotYouIdentities.Samwise))
            {
                var svc = RestService.For<ITransitHttpClient>(client);
                var itemsResponse = await svc.GetOutboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count == 1);

                var singleItemResponse = await svc.GetOutboxItem(items.Results.First().Id);

                Assert.IsTrue(singleItemResponse.IsSuccessStatusCode);
                var singleItem = singleItemResponse.Content;
                Assert.IsNotNull(singleItem);
                Assert.IsTrue(singleItem.Id == items.Results.First().Id);
            }
        }

        [Test(Description = "")]
        public async Task CanUpdateOutboxItemPriority()
        {
            await SendTransfer();
            using (var client = _scaffold.CreateOwnerApiHttpClient(DotYouIdentities.Samwise))
            {
                var svc = RestService.For<ITransitHttpClient>(client);
                var itemsResponse = await svc.GetOutboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);

                var itemId = items.Results.First().Id;

                var updateResponse = await svc.UpdateOutboxItemPriority(itemId, 999);

                Assert.IsTrue(updateResponse.IsSuccessStatusCode);

                var singleItemResponse = await svc.GetOutboxItem(itemId);

                Assert.IsTrue(singleItemResponse.IsSuccessStatusCode);
                var singleItem = singleItemResponse.Content;
                Assert.IsNotNull(singleItem);
                Assert.IsTrue(singleItem.Id == items.Results.First().Id);
                Assert.IsTrue(singleItem.Priority == 999);
            }
        }

        private async Task SendTransfer()
        {
            var appSharedSecret = new SecureKey(new byte[] {1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1});
            var transferIv = ByteArrayUtil.GetRndByteArray(16);

            var keyHeader = new KeyHeader()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                AesKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16))
            };

            var metadataJson = "{metadata:true, message:'pie on sky}";
            var metaDataCipher = UploadEncryptionUtils.GetEncryptedStream(metadataJson, keyHeader);

            var payloadJson = "{payload:true, image:'b64 data'}";
            var payloadCipher = UploadEncryptionUtils.GetEncryptedStream(payloadJson, keyHeader);

            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, appSharedSecret.GetKey());

            var b = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ekh));
            var encryptedKeyHeaderStream = new MemoryStream(b);

            var recipientList = new RecipientList {Recipients = new List<DotYouIdentity>() {DotYouIdentities.Frodo}};
            var recipientJson = JsonConvert.SerializeObject(recipientList);

            var recipientCipher = UploadEncryptionUtils.GetAppSharedSecretEncryptedStream(recipientJson, transferIv, appSharedSecret.GetKey());

            keyHeader.AesKey.Wipe();
            appSharedSecret.Wipe();

            using (var client = _scaffold.CreateOwnerApiHttpClient(DotYouIdentities.Samwise))
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
                Assert.IsFalse(transferResult.FileId == Guid.Empty, "FileId was not set");
                Assert.IsTrue(transferResult.RecipientStatus.Count == 1, "Too many recipient results returned");
                Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(DotYouIdentities.Frodo), "Could not find matching recipient");
                Assert.IsTrue(transferResult.RecipientStatus[DotYouIdentities.Frodo] == TransferStatus.TransferKeyCreated);

                //there should be a record in the outbox for this transfer
                var outboxItemsResponse = await transitSvc.GetOutboxItems(1, 100);
                Assert.IsTrue(outboxItemsResponse.IsSuccessStatusCode);
            }
        }
    }
}