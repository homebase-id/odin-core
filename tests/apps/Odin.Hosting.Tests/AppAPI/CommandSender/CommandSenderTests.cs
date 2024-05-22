using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Apps.CommandMessaging;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Incoming;
using Odin.Services.Peer.Incoming.Drive;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.App.Commands;
using Odin.Hosting.Tests.AppAPI.Transit;
using Odin.Hosting.Tests.AppAPI.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.CommandSender
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

        [Test(Description = "Test sending and receiving a command message")]
        public async Task CanSendAndReceiveCommand()
        {
            var targetDrive = TargetDrive.NewTargetDrive();
            int someFileType = 1948;

            var scenarioCtx = await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);

            var senderTestContext = scenarioCtx.AppContexts[TestIdentities.Frodo.OdinId];

            //Setup the app on all recipient DIs
            var recipientContexts = new Dictionary<OdinId, TestAppContext>()
            {
                { TestIdentities.Samwise.OdinId, scenarioCtx.AppContexts[TestIdentities.Samwise.OdinId] },
                { TestIdentities.Merry.OdinId, scenarioCtx.AppContexts[TestIdentities.Merry.OdinId] },
                { TestIdentities.Pippin.OdinId, scenarioCtx.AppContexts[TestIdentities.Pippin.OdinId] }
            };

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new()
                {
                    Drive = targetDrive
                },
                TransitOptions = new()
                {
                    Recipients = recipientContexts.Keys.Select(k => k.ToString()).ToList(),
                    UseGlobalTransitId = true,
                    IsTransient = false
                }
            };

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = true,
                AppData = new()
                {
                    FileType = someFileType,
                    Content = "{some:'file content'}",
                },
                AccessControlList = AccessControlList.Connected
            };

            var options = new TransitTestUtilsOptions()
            {
                DisconnectIdentitiesAfterTransfer = false,
                ProcessOutbox = true,
                ProcessTransitBox = true
            };

            var originalFileSendResult = await _scaffold.AppApi.TransferFile(senderTestContext, recipientContexts, instructionSet, fileMetadata, options);
            Assert.IsNotNull(originalFileSendResult);
            Assert.IsNotNull(originalFileSendResult.GlobalTransitId, "There should be a GlobalTransitId since we set transit options UseGlobalTransitId = true");

            var command = new CommandMessage()
            {
                Code = 100,
                JsonMessage = OdinSystemSerializer.Serialize(new { reaction = ":)" }),
                GlobalTransitIdList = [originalFileSendResult.GlobalTransitId.GetValueOrDefault()],
                Recipients = instructionSet.TransitOptions.Recipients // same as we sent the file
            };

            //
            // Send the command
            //
            var client = _scaffold.AppApi.CreateAppApiHttpClient(senderTestContext);
            {
                var cmdService = RefitCreator.RestServiceFor<IAppCommandSenderHttpClient>(client, senderTestContext.SharedSecret);
                var sendCommandResponse = await cmdService.SendCommand(new SendCommandRequest()
                {
                    TargetDrive = targetDrive,
                    Command = command
                });

                Assert.That(sendCommandResponse.IsSuccessStatusCode, Is.True);
                Assert.That(sendCommandResponse.Content, Is.Not.Null);
                var commandResult = sendCommandResponse.Content;

                //TODO: add checks that the command was sent
                // Assert.That(commandResult.File, Is.Not.Null);
                // Assert.That(commandResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                // Assert.IsTrue(commandResult.File.TargetDrive.IsValid());

                Assert.That(commandResult.RecipientStatus, Is.Not.Null);
                Assert.IsTrue(commandResult.RecipientStatus.Count == 3);

                await _scaffold.OldOwnerApi.ProcessOutbox(senderTestContext.Identity, batchSize: commandResult.RecipientStatus.Count + 100);
            }

            await AssertCommandReceived(recipientContexts[TestIdentities.Samwise.OdinId], command, originalFileSendResult, senderTestContext.Identity);
            await AssertCommandReceived(recipientContexts[TestIdentities.Merry.OdinId], command, originalFileSendResult, senderTestContext.Identity);
            await AssertCommandReceived(recipientContexts[TestIdentities.Pippin.OdinId], command, originalFileSendResult, senderTestContext.Identity);

            //
            // validate frodo no longer as the file associated w/ the command
            // 

            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Frodo.OdinId, TestIdentities.Samwise.OdinId);
            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Frodo.OdinId, TestIdentities.Merry.OdinId);
            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Frodo.OdinId, TestIdentities.Pippin.OdinId);

            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId);
            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Samwise.OdinId, TestIdentities.Pippin.OdinId);

            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Pippin.OdinId, TestIdentities.Merry.OdinId);
        }

        private async Task AssertCommandReceived(TestAppContext recipientAppContext, CommandMessage command, AppTransitTestUtilsContext originalFileSendResult, OdinId sender)
        {
            var drive = originalFileSendResult.UploadedFile.TargetDrive;

            var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientAppContext);
            {
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = drive });
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var cmdService = RefitCreator.RestServiceFor<IAppCommandSenderHttpClient>(client, recipientAppContext.SharedSecret);
                var getUnprocessedCommandsResponse = await cmdService.GetUnprocessedCommands(new GetUnprocessedCommandsRequest()
                {
                    TargetDrive = drive,
                    Cursor = "" // ??
                });

                Assert.That(getUnprocessedCommandsResponse.IsSuccessStatusCode, Is.True);
                Assert.That(getUnprocessedCommandsResponse.Content, Is.Not.Null);
                var resultSet = getUnprocessedCommandsResponse.Content;

                var receivedCommand = resultSet.ReceivedCommands.SingleOrDefault();
                Assert.IsNotNull(receivedCommand, "There should be a single command");
                Assert.IsFalse(receivedCommand.Id == Guid.Empty);
                Assert.IsTrue(receivedCommand.ClientJsonMessage == command.JsonMessage, "received json message should match the sent message");
                Assert.IsTrue(receivedCommand.ClientCode == command.Code);
                Assert.IsTrue(((OdinId)receivedCommand.Sender) == sender);
                var gtid = receivedCommand.GlobalTransitIdList.SingleOrDefault();
                Assert.IsNotNull(gtid, "There should be only 1 GlobalTransitId returned");
                Assert.IsTrue(gtid == originalFileSendResult.GlobalTransitId);


                //cmdService.MarkCommandsComplete()
                //
            }
        }
    }
}