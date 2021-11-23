﻿using System;
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

namespace Youverse.Hosting.Tests.Transit
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
        //     // using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise, false, true))
        //     // {
        //     // }
        // }

        [Test(Description = "")]
        public async Task CanGetInboxList()
        {
            await SendTransfer();
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo, false, true))
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
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo, false, true))
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
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo, false, true))
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
            var sender = _scaffold.Samwise;
            var recipient = _scaffold.Frodo;
            
            var appSharedSecret = new SecureKey(Guid.Parse("4fc5b0fd-e21e-427d-961b-a2c7a18f18c5").ToByteArray());

            var keyHeader = new KeyHeader()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                AesKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16))
            };

            var metadataJson = "{metadata:true, message:'pie on sky}";
            var metaDataCipher = TransitTestUtils.GetEncryptedStream(metadataJson, keyHeader);

            var payloadJson = "{payload:true, image:'b64 data'}";
            var payloadCipher = TransitTestUtils.GetEncryptedStream(payloadJson, keyHeader);

            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, appSharedSecret.GetKey());

            var b = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ekh));
            var encryptedKeyHeaderStream = new MemoryStream(b);

            var recipientList = new RecipientList { Recipients = new List<DotYouIdentity>() { recipient } };
            var recipientJson = JsonConvert.SerializeObject(recipientList);

            var recipientCipher = TransitTestUtils.GetAppSharedSecretEncryptedStream(recipientJson, ekh.Iv, appSharedSecret.GetKey());

            keyHeader.AesKey.Wipe();
            appSharedSecret.Wipe();
            
            using (var client = _scaffold.CreateHttpClient(sender))
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
                Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), "Could not find matching recipient");
                Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated);
            }
            
            System.Threading.Thread.Sleep(10 * 1000);
            
        }
    }
}