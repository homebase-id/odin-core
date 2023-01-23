using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.ClientToken.App;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.AppAPI.Utils;


namespace Youverse.Hosting.Tests.AppAPI.CommandSender
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
            int SomeFileType = 1948;

            var scenarioCtx = await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);

            var senderTestContext = scenarioCtx.AppContexts[TestIdentities.Frodo.DotYouId];

            //Setup the app on all recipient DIs
            var recipientContexts = new Dictionary<DotYouIdentity, TestAppContext>()
            {
                { TestIdentities.Samwise.DotYouId, scenarioCtx.AppContexts[TestIdentities.Samwise.DotYouId] },
                { TestIdentities.Merry.DotYouId, scenarioCtx.AppContexts[TestIdentities.Merry.DotYouId] },
                { TestIdentities.Pippin.DotYouId, scenarioCtx.AppContexts[TestIdentities.Pippin.DotYouId] }
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
                ContentType = "application/json",
                PayloadIsEncrypted = true,
                AppData = new()
                {
                    FileType = SomeFileType,
                    JsonContent = "{some:'file content'}",
                },
                AccessControlList = AccessControlList.OwnerOnly
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
                JsonMessage = DotYouSystemSerializer.Serialize(new { reaction = ":)" }),
                GlobalTransitIdList = new List<Guid>() { originalFileSendResult.GlobalTransitId.GetValueOrDefault() },
                Recipients = instructionSet.TransitOptions.Recipients // same as we sent the file
            };

            //
            // Send the command
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderTestContext))
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

            await AssertCommandReceived(recipientContexts[TestIdentities.Samwise.DotYouId], command, originalFileSendResult, senderTestContext.Identity);
            await AssertCommandReceived(recipientContexts[TestIdentities.Merry.DotYouId], command, originalFileSendResult, senderTestContext.Identity);
            await AssertCommandReceived(recipientContexts[TestIdentities.Pippin.DotYouId], command, originalFileSendResult, senderTestContext.Identity);

            //
            // validate frodo no longer as the file associated w/ the command
            // 

            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Frodo.DotYouId, TestIdentities.Samwise.DotYouId);
            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Frodo.DotYouId, TestIdentities.Merry.DotYouId);
            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Frodo.DotYouId, TestIdentities.Pippin.DotYouId);

            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Samwise.DotYouId, TestIdentities.Merry.DotYouId);
            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Samwise.DotYouId, TestIdentities.Pippin.DotYouId);

            await _scaffold.OldOwnerApi.DisconnectIdentities(TestIdentities.Pippin.DotYouId, TestIdentities.Merry.DotYouId);
        }

        private async Task AssertCommandReceived(TestAppContext recipientAppContext, CommandMessage command, AppTransitTestUtilsContext originalFileSendResult, DotYouIdentity sender)
        {
            var drive = originalFileSendResult.UploadedFile.TargetDrive;

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientAppContext))
            {
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessIncomingInstructions(new ProcessTransitInstructionRequest() { TargetDrive = drive });
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var cmdService = RefitCreator.RestServiceFor<IAppCommandSenderHttpClient>(client, recipientAppContext.SharedSecret);
                var getUnprocessedCommandsResponse = await cmdService.GetUnprocessedCommands(new GetUnproccessedCommandsRequest()
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
                Assert.IsTrue(((DotYouIdentity)receivedCommand.Sender) == sender);
                var gtid = receivedCommand.GlobalTransitIdList.SingleOrDefault();
                Assert.IsNotNull(gtid, "There should be only 1 GlobalTransitId returned");
                Assert.IsTrue(gtid == originalFileSendResult.GlobalTransitId);


                //cmdService.MarkCommandsComplete()
                //
            }
        }
    }
}