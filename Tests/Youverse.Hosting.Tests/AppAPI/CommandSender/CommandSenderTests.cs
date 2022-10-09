using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.ClientToken.App;

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

        [Test(Description = "Test Upload only; no expire, no drive; no transfer")]
        public async Task CanSendAndReceiveCommand()
        {
            Guid appId = Guid.NewGuid();
            var drive = TargetDrive.NewTargetDrive();

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


            var command = new CommandMessage()
            {
                Drive = frodoAppContext.TargetDrive,
                JsonMessage = DotYouSystemSerializer.Serialize(new { reaction = ":)" }),
                GlobalTransitIdList = new List<Guid>() { Guid.NewGuid() },
                Recipients = new List<string>() { TestIdentities.Samwise.DotYouId, TestIdentities.Merry.DotYouId }
            };

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

                // Assert.That(commandResult.File, Is.Not.Null);
                // Assert.That(commandResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                // Assert.IsTrue(commandResult.File.TargetDrive.IsValid());

                Assert.That(commandResult.RecipientStatus, Is.Not.Null);
                Assert.IsTrue(commandResult.RecipientStatus.Count == 2, "Too many recipient results returned");

                //
            }
        }
    }
}