using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public class DriveStorageTests
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
        public async Task CanUploadUsingAppDrive()
        {
            var identity = TestIdentities.Samwise;
            var (appId, deviceUid, authResult, appSharedSecretKey) = await _scaffold.SetupSampleApp(identity);
            
            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            //Note: in this test we're using teh app's
            //drive.  the will be set by the server
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
                    JsonContent = JsonConvert.SerializeObject(new {message = "We're going to the beach"})
                }
            };

            var metadataJson = JsonConvert.SerializeObject(metadata);
            var metaDataCipher = Utils.EncryptAes(metadataJson, transferIv, _scaffold.AppSharedSecret);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, _scaffold.AppSharedSecret);

            var b = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ekh));
            var encryptedKeyHeaderStream = new MemoryStream(b);

            keyHeader.AesKey.Wipe();
            
            using (var client = _scaffold.CreateAppApiHttpClient(identity, authResult))
            {
                var uploadSvc = RestService.For<IDriveStorageHttpClient>(client);

                var response = await uploadSvc.StoreUsingAppDrive(
                    new StreamPart(encryptedKeyHeaderStream, "tekh.encrypted", "application/json", "tekh"),
                    new StreamPart(metaDataCipher, "metadata.encrypted", "application/json", "metadata"),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", "payload"));

                Assert.IsTrue(response.IsSuccessStatusCode);
                var uploadResult = response.Content;
                Assert.IsNotNull(uploadResult);
                Assert.That(uploadResult.FileId, Is.Not.EqualTo(Guid.Empty), "FileId was not set");
                Assert.That(uploadResult.DriveId, Is.Not.EqualTo(Guid.Empty), "FileId was not set");

            }
        }
    }
}