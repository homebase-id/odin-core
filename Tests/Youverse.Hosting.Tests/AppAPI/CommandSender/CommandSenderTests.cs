using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.ClientToken.App;
using Youverse.Hosting.Tests.AppAPI.CommandSender;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public class CommandSenderTests
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
        public async Task CanSendAndReceiveCommand()
        {
            var identity = TestIdentities.Frodo;
            var testContext = await _scaffold.OwnerApi.SetupTestSampleApp(identity);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();
            var sba = testContext.SharedSecret.ToSensitiveByteArray();

            var command = new CommandMessage()
            {
                Drive = testContext.TargetDrive,
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sba),
                JsonMessage = DotYouSystemSerializer.Serialize(new { reaction = ":)" }),
                GlobalTransitId = new List<Guid>() { Guid.NewGuid() },
            };

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(identity.DotYouId, testContext.ClientAuthenticationToken))
            {
                var cmdService = RestService.For<IAppCommandSenderHttpClient>(client);
                var sendCommandResponse = await cmdService.SendCommand(new SendCommandRequest()
                {
                    TransferIv = transferIv,
                    Command = command
                });

                Assert.That(sendCommandResponse.IsSuccessStatusCode, Is.True);
                Assert.That(sendCommandResponse.Content, Is.Not.Null);
                var commandResult = sendCommandResponse.Content;

                // Assert.That(commandResult.File, Is.Not.Null);
                // Assert.That(commandResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                // Assert.IsTrue(commandResult.File.TargetDrive.IsValid());

                Assert.That(commandResult.RecipientStatus, Is.Not.Null);
                Assert.IsTrue(commandResult.RecipientStatus.Count == 0, "Too many recipient results returned");

                //

                ////
                var targetDrive = commandResult.File.TargetDrive;
                var fileId = commandResult.File.FileId;

                //retrieve the file that was uploaded; decrypt; 
                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, testContext.SharedSecret);

                var fileResponse = await driveSvc.GetFileHeader(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });

                Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
                Assert.That(fileResponse.Content, Is.Not.Null);

                var clientFileHeader = fileResponse.Content;

                Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

                Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(descriptor.FileMetadata.ContentType));
                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));

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
                var payloadResponse = await driveSvc.GetPayload(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

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