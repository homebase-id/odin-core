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
            Guid appId = Guid.NewGuid();
            var drive = TargetDrive.NewTargetDrive();
            int SomeFileType = 1948;

            var frodoAppContext = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canReadConnections: true, drive, driveAllowAnonymousReads: false);
            var merryAppContext = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Merry, canReadConnections: true, drive, driveAllowAnonymousReads: false);
            var pippinAppContext = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Pippin, canReadConnections: true, drive, driveAllowAnonymousReads: false);
            var samAppContext = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canReadConnections: true, drive, driveAllowAnonymousReads: false);

            var senderTestContext = frodoAppContext;

            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Samwise.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Merry.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Pippin.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Samwise.DotYouId, TestIdentities.Merry.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Samwise.DotYouId, TestIdentities.Pippin.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Pippin.DotYouId, TestIdentities.Merry.DotYouId);

            var recipientContexts = new Dictionary<DotYouIdentity, TestSampleAppContext>()
            {
                { TestIdentities.Samwise.DotYouId, samAppContext },
                { TestIdentities.Merry.DotYouId, merryAppContext },
                { TestIdentities.Pippin.DotYouId, pippinAppContext }
            };

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new()
                {
                    Drive = drive
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
                AccessControlList = AccessControlList.NewOwnerOnly
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
                Drive = frodoAppContext.TargetDrive,
                JsonMessage = DotYouSystemSerializer.Serialize(new { reaction = ":)" }),
                GlobalTransitIdList = new List<Guid>() { originalFileSendResult.GlobalTransitId.GetValueOrDefault() },
                Recipients = instructionSet.TransitOptions.Recipients // same as we sent the file
            };

            //
            // Send the command
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderTestContext.Identity, senderTestContext.ClientAuthenticationToken))
            {
                var cmdService = RefitCreator.RestServiceFor<IAppCommandSenderHttpClient>(client, senderTestContext.SharedSecret);
                var sendCommandResponse = await cmdService.SendCommand(new SendCommandRequest()
                {
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

                await _scaffold.OwnerApi.ProcessOutbox(senderTestContext.Identity, batchSize: commandResult.RecipientStatus.Count + 100);
            }

            await AssertCommandReceived(samAppContext, command, originalFileSendResult);
            await AssertCommandReceived(merryAppContext, command, originalFileSendResult);
            await AssertCommandReceived(pippinAppContext, command, originalFileSendResult);

            //
            // validate frodo no longer as the file associated w/ the command
            // 
            
            await _scaffold.OwnerApi.DisconnectIdentities(TestIdentities.Frodo.DotYouId, TestIdentities.Samwise.DotYouId);
            await _scaffold.OwnerApi.DisconnectIdentities(TestIdentities.Frodo.DotYouId, TestIdentities.Merry.DotYouId);
            await _scaffold.OwnerApi.DisconnectIdentities(TestIdentities.Frodo.DotYouId, TestIdentities.Pippin.DotYouId);

            await _scaffold.OwnerApi.DisconnectIdentities(TestIdentities.Samwise.DotYouId, TestIdentities.Merry.DotYouId);
            await _scaffold.OwnerApi.DisconnectIdentities(TestIdentities.Samwise.DotYouId, TestIdentities.Pippin.DotYouId);

            await _scaffold.OwnerApi.DisconnectIdentities(TestIdentities.Pippin.DotYouId, TestIdentities.Merry.DotYouId);
        }

        private async Task AssertCommandReceived(TestSampleAppContext recipientAppContext, CommandMessage command, AppTransitTestUtilsContext originalFileSendResult)
        {
            var drive = command.Drive;

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientAppContext.Identity, recipientAppContext.ClientAuthenticationToken))
            {
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessIncomingInstructions(new ProcessInstructionRequest() { TargetDrive = drive });
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
                Assert.IsTrue(receivedCommand.Drive == command.Drive);
                Assert.IsFalse(receivedCommand.Id == Guid.Empty);
                Assert.IsTrue(receivedCommand.ClientJsonMessage == command.JsonMessage, "received json message should match the sent message");

                var gtid = receivedCommand.GlobalTransitIdList.SingleOrDefault();
                Assert.IsNotNull(gtid, "There should be only 1 GlobalTransitId returned");
                Assert.IsTrue(gtid == originalFileSendResult.GlobalTransitId);
                var matchedFile = receivedCommand.MatchingFiles.SingleOrDefault();
                Assert.IsNotNull(matchedFile, "there should be only one matched file");
                Assert.IsTrue(matchedFile.FileId != originalFileSendResult.UploadedFile.FileId, "matched file should NOT have same Id as the one we uploaded since it was sent to a new identity");
                Assert.IsTrue(matchedFile.FileMetadata.GlobalTransitId == originalFileSendResult.GlobalTransitId, "The matched file should have the same global transit id as the file orignally sent");
                Assert.IsTrue(matchedFile.FileMetadata.AppData.JsonContent == originalFileSendResult.UploadFileMetadata.AppData.JsonContent,
                    "matched file should have same JsonContent as the on we uploaded");
                Assert.IsTrue(matchedFile.FileMetadata.AppData.FileType == originalFileSendResult.UploadFileMetadata.AppData.FileType);

                //cmdService.MarkCommandsComplete()
                //
            }
        }
    }
}