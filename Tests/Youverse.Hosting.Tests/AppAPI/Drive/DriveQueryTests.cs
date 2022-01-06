using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    public class DriveQueryTests
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
        public async Task FailsWhenNoValidIndex()
        {
           Assert.Inconclusive("TODO");
        }

        // [Test]
        // public async Task CanQueryDriveByCategory()
        // {
        // }
        //
        // [Test]
        // public async Task CanQueryDriveByCategoryNoContent()
        // {
        // }

        [Test]
        public async Task CanQueryDriveRecentItems()
        {
            var identity = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            byte[] deviceUid = Guid.NewGuid().ToByteArray();

            var app = await _scaffold.AddApp(identity, appId, true);
            await _scaffold.AddAppDevice(identity, appId, deviceUid);
            var authCode = await _scaffold.CreateAppSession(identity, appId, deviceUid);
            var authResult = await _scaffold.ExchangeAppAuthCode(identity, authCode, appId, deviceUid);

            await UploadFile(identity, authResult);
            
            using (var client = _scaffold.CreateAppApiHttpClient(identity, authResult))
            {
                var svc = RestService.For<IDriveQueryClient>(client);

                var response = await svc.GetRecentlyCreatedItems(app.DriveId.GetValueOrDefault(), true, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                //TODO: what to test here?
                Assert.IsTrue(page.Results.Count > 0);
            }
        }
        
        [Test]
        public async Task CanQueryDriveRecentItemsNoContent()
        {
            var identity = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            byte[] deviceUid = Guid.NewGuid().ToByteArray();

            var app = await _scaffold.AddApp(identity, appId, true);
            await _scaffold.AddAppDevice(identity, appId, deviceUid);
            var authCode = await _scaffold.CreateAppSession(identity, appId, deviceUid);
            var authResult = await _scaffold.ExchangeAppAuthCode(identity, authCode, appId, deviceUid);

            await UploadFile(identity, authResult);
            
            using (var client = _scaffold.CreateAppApiHttpClient(identity,authResult))
            {
                var svc = RestService.For<IDriveQueryClient>(client);

                var response = await svc.GetRecentlyCreatedItems(app.DriveId.GetValueOrDefault(), false, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                Assert.IsTrue(page.Results.Count > 0);
                Assert.IsTrue(page.Results.All(item => string.IsNullOrEmpty(item.JsonContent)), "One or more items had content");
            }
        }
        
        
        
        private async Task UploadFile(DotYouIdentity identity, DotYouAuthenticationResult authResult)
        {
            
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