﻿using System;
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
    public class UploadFileTests
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

            var testContext = await _scaffold.SetupTestSampleApp(identity);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var sba = testContext.AppSharedSecretKey.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sba),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    AppData = new()
                    {
                        Tags = new List<Guid>() {Guid.NewGuid(), Guid.NewGuid()},
                        ContentIsComplete = true,
                        JsonContent = JsonConvert.SerializeObject(new {message = "We're going to the beach; this is encrypted by the app"})
                    }
                },
            };

            var key = testContext.AppSharedSecretKey.ToSensitiveByteArray();
            var fileDescriptorCipher = Utils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadDataRaw = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadDataRaw);

            using (var client = _scaffold.CreateAppApiHttpClient(identity, testContext.AuthResult))
            {
                var transitSvc = RestService.For<ITransitTestHttpClient>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(uploadResult.File.DriveIdentifier, Is.Not.EqualTo(Guid.Empty));

                Assert.That(uploadResult.RecipientStatus, Is.Not.Null);
                Assert.IsTrue(uploadResult.RecipientStatus.Count == 0, "Too many recipient results returned");

                //

                ////
                var driveIdentifier = uploadResult.File.DriveIdentifier;

                var fileId = uploadResult.File.FileId;

                //retrieve the file that was uploaded; decrypt; 
                var driveSvc = RestService.For<IDriveStorageHttpClient>(client);

                var fileResponse = await driveSvc.GetFileHeader(uploadResult.File.DriveIdentifier, fileId);

                Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
                Assert.That(fileResponse.Content, Is.Not.Null);

                var clientFileHeader = fileResponse.Content;

                Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

                Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(descriptor.FileMetadata.ContentType));
                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));

                Assert.That(clientFileHeader.EncryptedKeyHeader, Is.Not.Null);
                Assert.That(clientFileHeader.EncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(clientFileHeader.EncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(clientFileHeader.EncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()));
                Assert.That(clientFileHeader.EncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var decryptedKeyHeader = clientFileHeader.EncryptedKeyHeader.DecryptAesToKeyHeader(ref key);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                var fileKey = decryptedKeyHeader.AesKey;
                Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

                //get the payload and decrypt, then compare
                var payloadResponse = await driveSvc.GetPayload(driveIdentifier, fileId);
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream) payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    Key: ref aesKey,
                    IV: decryptedKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadDataRaw);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);

                decryptedKeyHeader.AesKey.Wipe();
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();
        }
    }
}