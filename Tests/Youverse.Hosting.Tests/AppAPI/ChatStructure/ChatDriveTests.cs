using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure
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
            var chatApps = await this.InitializeApps();

            var samwiseChatApp = chatApps[TestIdentities.Samwise.DotYouId];
            var merryChatApp = chatApps[TestIdentities.Merry.DotYouId];

            var samAndMerryConversation = await samwiseChatApp.ConversationDefinitionService.StartConversation((DotYouIdentity)merryChatApp.Identity);

            //
            // Validate sam has conversation file
            //
            var samConvo = await samwiseChatApp.ConversationDefinitionService.GetConversation(samAndMerryConversation.Id);
            Assert.IsNotNull(samConvo);
            Assert.IsTrue(samConvo.RecipientDotYouId == merryChatApp.Identity);

            //
            // Sam sends a message
            //
            await samwiseChatApp.MessageService.SendMessage(new ChatMessage()
            {
                Id = Guid.NewGuid(),
                ConversationId = samAndMerryConversation.Id,
                Text = "Prancing pony time, lezzgo!",
            });

            samwiseChatApp.SynchronizeData();

            // Validate sam has sent message
            var samMessages = await samwiseChatApp.MessageService.GetMessages(samConvo.Id);
            Assert.IsTrue(samMessages.Count() == 1);

            ////////////
            ////////////
            ////////////

            //
            // Merry's app syncs data
            merryChatApp.SynchronizeData();

            //
            // Validate merry has the conversation file
            //
            var merrysConvo = await merryChatApp.ConversationDefinitionService.GetConversation(samAndMerryConversation.Id);
            Assert.IsNotNull(merrysConvo);
            Assert.IsTrue(merrysConvo.RecipientDotYouId == samwiseChatApp.Identity);

            //
            // Valid date merry has received message
            //
            var merryMessages1 = await merryChatApp.MessageService.GetMessages(samAndMerryConversation.Id);
            Assert.IsNotNull(merryMessages1.SingleOrDefault(msg => msg.Message.Text == "Prancing pony time, lezzgo!"));

            //
            // Merry sends reply to sam
            //
            await merryChatApp.MessageService.SendMessage(new ChatMessage()
            {
                Id = Guid.NewGuid(),
                Text = "Only if they have South Farthing to go with",
                ConversationId = samAndMerryConversation.Id
            });

            merryChatApp.SynchronizeData();
            samwiseChatApp.SynchronizeData();

            var samMessages2 = await samwiseChatApp.MessageService.GetMessages(samConvo.Id);
            Assert.IsTrue(samMessages2.Count() == 2);

            //Validate sam received a reply
            var msgFromMerry = samMessages2.SingleOrDefault(x => x.Message.Text == "Only if they have South Farthing to go with");
            Assert.IsNotNull(msgFromMerry);
            Assert.IsTrue(msgFromMerry.Message.Sender == merryChatApp.Identity);

            // Assert.IsTrue(msgFromMerry.Reactions.Count() == 1);
            await RenderMessages(samwiseChatApp, samAndMerryConversation.Id);
        }

        private async Task RenderMessages(ChatApp app, Guid convoId)
        {
            Console.WriteLine($"\n\n{app.Identity} Messages");

            var messages = await app.MessageService.GetMessages(convoId);
            foreach (var msg in messages)
            {
                var receivedDate = UnixTimeStampToDateTime(msg.ReceivedTimestamp);
                var senderText = string.IsNullOrEmpty(msg.Message.Sender) ? app.Identity : msg.Message.Sender;
                Console.WriteLine($"({receivedDate}) {senderText} says \t\t{msg.Message.Text}");

                foreach (var msgReaction in msg.Reactions)
                {
                    Console.Write("\t");
                    Console.WriteLine($"{msgReaction.Sender} -> {msgReaction.ReactionValue}");
                }
            }
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddMilliseconds(unixTimeStamp).ToLocalTime();
            return dateTime;
        }

        private Task SynchronizeData(Dictionary<string, ChatApp> chatApps)
        {
            //Note: in this example, the group does not exist even for frodo until he processes the command
            // sync data for all members
            foreach (var (key, app) in chatApps)
            {
                app.SynchronizeData();
            }

            return Task.CompletedTask;
        }

        public async Task<Dictionary<string, ChatApp>> InitializeApps()
        {
            var apps = new Dictionary<string, ChatApp>(StringComparer.InvariantCultureIgnoreCase);

            var scenarioCtx = await _scaffold.Scenarios.CreateConnectedHobbits(ChatApiConfig.Drive);

            foreach (var testAppContext in scenarioCtx.AppContexts)
            {
                var chatApp = new ChatApp(testAppContext.Value, _scaffold);
                apps.Add(testAppContext.Key, chatApp);
            }

            // foreach (var (dotYouId, identity) in TestIdentities.All)
            // {
            // var testAppContext = await _scaffold.OwnerApi.SetupTestSampleApp(ChatApiConfig.AppId, identity, canReadConnections: true, ChatApiConfig.Drive, driveAllowAnonymousReads: false);
            // var chatApp = new ChatApp(testAppContext, _scaffold);
            // apps.Add(dotYouId, chatApp);
            // }

            return apps;
        }
    }
}