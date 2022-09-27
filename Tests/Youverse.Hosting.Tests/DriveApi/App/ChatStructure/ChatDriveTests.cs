using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Util;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;

namespace Youverse.Hosting.Tests.DriveApi.App.ChatStructure
{
    public class ChatDriveTests
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

        [Test]
        public void CanXorMatchTwoIdentitiesRegardlessOfWhichStartsChat()
        {
            var frodoBytes = TestIdentities.Frodo.DotYouId.ToGuidIdentifier().ToByteArray();
            var samBytes = TestIdentities.Samwise.DotYouId.ToGuidIdentifier().ToByteArray();

            var frodoFirst = ByteArrayUtil.EquiByteArrayXor(frodoBytes, samBytes);
            var samFirst = ByteArrayUtil.EquiByteArrayXor(samBytes, frodoBytes);

            Assert.IsTrue(frodoFirst.SequenceEqual(samFirst));
        }

        [Test]
        public async Task SendMessage()
        {
            var api = new ChatApi(_scaffold, TestIdentities.All);
            await api.Initialize();

            await api.SendChatMessage(sender: TestIdentities.Frodo, recipient: TestIdentities.Samwise, "Let's roll kato");

            //TODO
            string prevCursorState = "";
            var (frodoSentMessages, frodoCursorState) = await api.GetMessages(TestIdentities.Frodo, prevCursorState);
            var (samwiseReceivedMessages, samwiseCursorState) = await api.GetMessages(TestIdentities.Samwise, prevCursorState);

            //CollectionAssert.AreEquivalent(frodoSentMessages.ToList(), samwiseReceivedMessages.ToList());
        }
    }

    public class ChatApi
    {
        private readonly WebScaffold _scaffold;
        private readonly Dictionary<string, TestIdentity> _participants;
        private readonly Dictionary<DotYouIdentity, TestSampleAppContext> _participantIdentityMap;

        private const int _chatFileType = 101;

        public readonly Guid ChatAppId = Guid.Parse("99888555-4444-0000-4444-000000004444");

        public readonly TargetDrive ChatDrive = new TargetDrive()
        {
            Alias = Guid.Parse("99888555-0000-0000-0000-000000004445"),
            Type = Guid.Parse("11888555-0000-0000-0000-000000001111")
        };

        public ChatApi(WebScaffold scaffold, Dictionary<string, TestIdentity> participants)
        {
            _scaffold = scaffold;
            _participants = participants;
            _participantIdentityMap = new Dictionary<DotYouIdentity, TestSampleAppContext>();
        }

        public async Task Initialize()
        {
            foreach (var (key, identity) in _participants)
            {
                var testAppContext = await _scaffold.OwnerApi.SetupTestSampleApp(ChatAppId, identity, canReadConnections: true, ChatDrive, driveAllowAnonymousReads: false);
                _participantIdentityMap.Add((DotYouIdentity)key, testAppContext);
            }

            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Samwise.DotYouId);
        }

        public async Task SendChatMessage(TestIdentity sender, TestIdentity recipient, string message)
        {
            var groupId = ByteArrayUtil.EquiByteArrayXor(sender.DotYouId.ToGuidIdentifier().ToByteArray(), recipient.DotYouId.ToGuidIdentifier().ToByteArray());

            //need to keep a conversation list file that includes all current conversations
            //or could xor all of my connections then hit the server
            
            
            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = DotYouSystemSerializer.Serialize(new ChatMessage() { Message = message }),
                    FileType = _chatFileType,
                    GroupId = groupId
                },
                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = SecurityGroupType.Owner
                }
            };

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new StorageOptions()
                {
                    Drive = ChatDrive,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },
                TransitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { recipient.DotYouId }
                }
            };

            var senderCtx = _participantIdentityMap[sender.DotYouId];
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(sender.DotYouId, senderCtx.ClientAuthenticationToken))
            {
                var keyHeader = KeyHeader.NewRandom16();
                var transferIv = instructionSet.TransferIv;

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var sharedSecret = senderCtx.SharedSecret.ToSensitiveByteArray();

                fileMetadata.PayloadIsEncrypted = true;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

                // string payloadData = "pickles and stuff";
                // var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);
                var payloadCipher = new MemoryStream();

                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

                if (instructionSet.TransitOptions?.Recipients != null)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count, "expected recipient count does not match");

                    foreach (var intendedRecipient in instructionSet.TransitOptions.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(intendedRecipient), $"Could not find matching recipient {recipient}");
                        Assert.IsTrue(transferResult.RecipientStatus[intendedRecipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                    }
                }

                keyHeader.AesKey.Wipe();
            }

            await ProcessOutbox(sender.DotYouId);
        }

        public async Task<(IEnumerable<ChatMessage> messages, string CursorState)> GetMessages(TestIdentity identity, string cursorState)
        {
            await ProcessIncomingTransfers(identity.DotYouId);

            var context = _participantIdentityMap[identity.DotYouId];
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(identity.DotYouId, context.ClientAuthenticationToken))
            {
                var svc = _scaffold.RestServiceFor<IDriveTestHttpClientForApps>(client, context.SharedSecret);
                var request = new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = context.TargetDrive,
                        FileType = new List<int>() { _chatFileType }
                    },

                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        CursorState = cursorState,
                        MaxRecords = 100,
                        IncludeMetadataHeader = true
                    }
                };

                var response = await svc.QueryBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content!;

                //Note: intentionally left decryption
                var messages = batch.SearchResults.Select(item =>
                    DotYouSystemSerializer.Deserialize<ChatMessage>(item.FileMetadata.AppData.JsonContent));

                return (messages, batch.CursorState);
            }
        }

        private async Task ProcessIncomingTransfers(DotYouIdentity identity, int delaySeconds = 0)
        {
            Task.Delay(delaySeconds).Wait();
            var ctx = _participantIdentityMap[identity];

            using (var rClient = _scaffold.AppApi.CreateAppApiHttpClient(identity, ctx.ClientAuthenticationToken))
            {
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                var resp = await transitAppSvc.ProcessIncomingTransfers(new ProcessTransfersRequest() { TargetDrive = ctx.TargetDrive });
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
            }
        }

        private async Task ProcessOutbox(DotYouIdentity identity)
        {
            await _scaffold.OwnerApi.ProcessOutbox(identity);
        }
    }

    public class ChatMessage
    {
        public string Message { get; set; }
    }
}