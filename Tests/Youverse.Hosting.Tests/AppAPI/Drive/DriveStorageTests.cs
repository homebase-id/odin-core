using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Hosting.Controllers.Owner.AppManagement;
using Youverse.Hosting.Tests.OwnerApi.Apps;

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
            //TODO: provision an app with a drive
            //this takes a call to the owner's api to create an app
            Guid applicationId = Guid.NewGuid();
            var app = this.AddAppWithDrive(applicationId, "Test-Upload-App");

            var appSharedSecret = new SecureKey(new byte[] {1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1});

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var file = new DriveFileId()
            {
                DriveId = Guid.Empty,
                FileId = Guid.Empty
            };

            var metadata = new FileMetaData(file)
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
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, appSharedSecret.GetKey());

            var b = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ekh));
            var encryptedKeyHeaderStream = new MemoryStream(b);

            keyHeader.AesKey.Wipe();
            appSharedSecret.Wipe();

            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise))
            {
                //sam to send frodo a data transfer, small enough to send it instantly

                var uploadSvc = RestService.For<IDriveStorageHttpClient>(client);

                var response = await uploadSvc.StoreUsingAppDrive(
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


        private async Task<AppRegistrationSimple> AddAppWithDrive(Guid applicationId, string name)
        {
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<IAppRegistrationTestHttpClient>(client);
                var payload = new AppRegistrationPayload
                {
                    Name = name,
                    ApplicationId = applicationId,
                    CreateDrive = true
                };

                var response = await svc.RegisterApp(payload);

                Assert.IsTrue(response.IsSuccessStatusCode);
                var appRegistrationSimple = response.Content;
                Assert.IsTrue(appRegistrationSimple.ApplicationId == payload.ApplicationId);
                Assert.IsTrue(appRegistrationSimple.DriveId.HasValue);
                Assert.IsTrue(appRegistrationSimple.DriveId != Guid.Empty);

                return appRegistrationSimple;
            }
        }
    }
}