﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveUploadAppTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test(Description = "Test Upload only; no expire, no drive; no transfer")]
        public async Task CanUploadFile()
        {
            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var payloadIv = ByteArrayUtil.GetRndByteArray(16);
            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = testContext.TargetDrive
                },
                Manifest = new UploadManifest()
                {
                    PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                    {
                        new()
                        {
                            Iv = payloadIv,
                            PayloadKey = WebScaffold.PAYLOAD_KEY
                        }
                    }
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var sba = testContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sba),
                FileMetadata = new()
                {
                    AllowDistribution = false,
                    IsEncrypted = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                        Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                    }
                },
            };

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadDataRaw = "{payload:true, image:'b64 data'}";

            var payloadKeyHeader = new KeyHeader()
            {
                Iv = payloadIv,
                AesKey = keyHeader.AesKey
            };

            var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadDataRaw);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(testContext);
            {
                var svc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await svc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());

                Assert.That(uploadResult.RecipientStatus, Is.Null);

                //

                ////
                var targetDrive = uploadResult.File.TargetDrive;
                var fileId = uploadResult.File.FileId;

                //retrieve the file that was uploaded; decrypt; 
                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, testContext.SharedSecret);

                var fileResponse = await driveSvc.GetFileHeaderAsPost(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });

                Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
                Assert.That(fileResponse.Content, Is.Not.Null);

                var clientFileHeader = fileResponse.Content;

                Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.Content, Is.EqualTo(descriptor.FileMetadata.AppData.Content));
                Assert.IsTrue(clientFileHeader.FileMetadata.Payloads.Count == 1);

                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref key);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                var fileKey = decryptedKeyHeader.AesKey;
                Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

                //get the payload and decrypt, then compare
                var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest()
                {
                    File = new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId },
                    Key = WebScaffold.PAYLOAD_KEY
                });
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    Key: aesKey,
                    IV: payloadKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadDataRaw);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);

                decryptedKeyHeader.AesKey.Wipe();
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();
        }

        [Test(Description = "")]
        public async Task CannotUploadEncryptedFileForAnonymousGroups()
        {
            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = testContext.TargetDrive
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var sba = testContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sba),
                FileMetadata = new()
                {
                    AllowDistribution = true,
                    IsEncrypted = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                        Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                    },
                    AccessControlList = new AccessControlList()
                    {
                        RequiredSecurityGroup = SecurityGroupType.Anonymous
                    }
                }
            };

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadDataRaw = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(testContext);
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.False(response.IsSuccessStatusCode);
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();
        }


        [Test(Description = "")]
        public async Task CanReuseUniqueIdAfterSoftDelete()
        {
            var identity = TestIdentities.Frodo;
            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var firstUniqueId = Guid.NewGuid();
            var firstFileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = true,
                AppData = new()
                {
                    Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                    Content = OdinSystemSerializer.Serialize(new { message = "some data" }),
                    UniqueId = firstUniqueId
                }
            };

            var firstFile = await ownerClient.Drive.UploadFile(FileSystemType.Standard, testContext.TargetDrive, firstFileMetadata);

            //
            // Validate File was uploaded
            //
            var getFirstFileResponse = await ownerClient.Drive.GetFileHeader(FileSystemType.Standard, firstFile.File);
            Assert.IsTrue(getFirstFileResponse.FileMetadata.AppData.Content == firstFileMetadata.AppData.Content);
            Assert.IsTrue(getFirstFileResponse.FileMetadata.AppData.UniqueId == firstFileMetadata.AppData.UniqueId);

            //
            // Can get first file by uniqueId
            //
            var getFirstFileByUniqueId = await ownerClient.Drive.QueryByUniqueId(FileSystemType.Standard, firstFile.File.TargetDrive, firstUniqueId);
            Assert.IsNotNull(getFirstFileByUniqueId);
            Assert.IsTrue(getFirstFileByUniqueId.FileMetadata.AppData.Content == firstFileMetadata.AppData.Content);
            Assert.IsTrue(getFirstFileByUniqueId.FileMetadata.AppData.UniqueId == firstFileMetadata.AppData.UniqueId);

            //
            // Delete the first file
            //

            await ownerClient.Drive.DeleteFile(firstFile.File);

            //
            // Validate first file is gone
            //

            var getFirstFileDeleted = await ownerClient.Drive.QueryByUniqueId(FileSystemType.Standard, firstFile.File.TargetDrive, firstUniqueId);
            Assert.IsNull(getFirstFileDeleted);

            //
            // Reuse the unique Id
            //

            var secondFileMeta = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = true,
                AppData = new()
                {
                    Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                    Content = OdinSystemSerializer.Serialize(new { message = "this is content in a second file that reuses a uniqueId" }),
                    UniqueId = firstUniqueId
                }
            };


            var secondFile = await ownerClient.Drive.UploadFile(FileSystemType.Standard, testContext.TargetDrive, secondFileMeta);

            //
            // Validate File was uploaded
            //
            var getSecondFileResponse = await ownerClient.Drive.GetFileHeader(FileSystemType.Standard, secondFile.File);
            Assert.IsTrue(getSecondFileResponse.FileMetadata.AppData.Content == secondFileMeta.AppData.Content);
            Assert.IsTrue(getSecondFileResponse.FileMetadata.AppData.UniqueId == secondFileMeta.AppData.UniqueId);

            //
            // Can get first file by uniqueId
            //
            var getSecondFileByUniqueId = await ownerClient.Drive.QueryByUniqueId(FileSystemType.Standard, firstFile.File.TargetDrive, firstUniqueId);
            Assert.IsNotNull(getSecondFileByUniqueId);
            Assert.IsTrue(getSecondFileByUniqueId.FileMetadata.AppData.Content == secondFileMeta.AppData.Content);
            Assert.IsTrue(getSecondFileByUniqueId.FileMetadata.AppData.UniqueId == secondFileMeta.AppData.UniqueId);
        }
    }
}