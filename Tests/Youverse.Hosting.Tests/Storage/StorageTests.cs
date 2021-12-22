using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

namespace Youverse.Hosting.Tests.Storage
{
    public class StorageTests
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


        [Test]
        public async Task TestBasicUpload()
        {
            var appSharedSecret = new SecureKey(new byte[] {1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1});

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = new KeyHeader()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                AesKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16))
            };
            
            var metadata = new FileMetaData()
            {
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ContentType = "application/json",
                AppData = new AppFileMetaData()
                {
                    CategoryId = Guid.Empty,
                    ContentIsComplete = true,
                    JsonContent = JsonConvert.SerializeObject(new {message = "We're going to the beach"})
                }
            };

            var metadataJson = JsonConvert.SerializeObject(metadata);
            var metaDataCipher = UploadEncryptionUtils.GetAppSharedSecretEncryptedStream(metadataJson, transferIv, appSharedSecret.GetKey());

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = UploadEncryptionUtils.GetEncryptedStream(payloadData, keyHeader);

            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, appSharedSecret.GetKey());

            var b = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ekh));
            var encryptedKeyHeaderStream = new MemoryStream(b);
            
            keyHeader.AesKey.Wipe();
            appSharedSecret.Wipe();

            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise))
            {
                //sam to send frodo a data transfer, small enough to send it instantly

                var uploadSvc = RestService.For<IUploadHttpClient>(client);

                var response = await uploadSvc.Store(
                    new StreamPart(encryptedKeyHeaderStream, "tekh.encrypted", "application/json", "tekh"),
                    new StreamPart(metaDataCipher, "metadata.encrypted", "application/json", "metadata"),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", "payload"));

                Assert.IsTrue(response.IsSuccessStatusCode);
                var uploadResult = response.Content;
                Assert.IsNotNull(uploadResult);
                Assert.IsFalse(uploadResult.FileId == Guid.Empty, "FileId was not set");
                Assert.IsTrue(uploadResult.RecipientStatus.Count == 1, "Too many recipient results returned");
                Assert.IsTrue(uploadResult.RecipientStatus.ContainsKey(_scaffold.Frodo), "Could not find matching recipient");
                Assert.IsTrue(uploadResult.RecipientStatus[_scaffold.Frodo] == TransferStatus.TransferKeyCreated);

            }
        }
    }
}