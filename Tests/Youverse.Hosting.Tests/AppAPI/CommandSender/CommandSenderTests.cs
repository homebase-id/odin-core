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

            var command = new CommandMessage()
            {
                Drive = testContext.TargetDrive,
                JsonMessage = DotYouSystemSerializer.Serialize(new { reaction = ":)" }),
                GlobalTransitIdList = new List<Guid>() { Guid.NewGuid() },
                Recipients = new List<string>() { TestIdentities.Samwise.DotYouId, TestIdentities.Merry.DotYouId }
            };

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(identity.DotYouId, testContext.ClientAuthenticationToken))
            {
                var cmdService = RestService.For<IAppCommandSenderHttpClient>(client);
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

            key.Wipe();
        }
    }
}