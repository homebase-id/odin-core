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
            
            //conversationId
            // var flutterUuidValue = new Guid("2c1b2834-1e3c-47ce-8e77-885bb5a1da55");
            // var g = new Guid(frodoFirst);
            // Assert.IsTrue(g == flutterUuidValue);

        }

        [Test]
        [Ignore("Not a critical test")]
        public async Task SendMessage()
        {
            var chatApps = await this.InitializeApps();

            var samwiseChatApp = chatApps[TestIdentities.Samwise.DotYouId];
            var merryChatApp = chatApps[TestIdentities.Merry.DotYouId];

            const string firstMessageFromSam = "Prancing pony time, lezzgo! 🍺🍺🍺";
            const string firstReplyFromMerry = "Only if they have South Farthing to go with 😶‍🤫";
            const string firstReplyFromSam = "totes mcgoats 🐐";
            const string merryReaction = "💨";

            // Conversation started by sam
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
            await samwiseChatApp.MessageFileService.SendMessage(
                id: Guid.NewGuid(),
                conversationId: samAndMerryConversation.Id,
                messageText: firstMessageFromSam
            );

            samwiseChatApp.SynchronizeData();

            // Validate sam has sent message
            var samMessages = await samwiseChatApp.MessageFileService.GetMessages(samConvo.Id);
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
            var merryMessages1 = await merryChatApp.MessageFileService.GetMessages(samAndMerryConversation.Id);
            var msgFromSam = merryMessages1.SingleOrDefault(msg => msg.Text == firstMessageFromSam);
            Assert.IsNotNull(msgFromSam);
            await merryChatApp.MessageFileService.NotifyMessageWasRead(msgFromSam.ConversationId, msgFromSam.Id);

            //
            // Merry sends reply to sam
            //
            await merryChatApp.MessageFileService.SendMessage(
                id: Guid.NewGuid(),
                messageText: firstReplyFromMerry,
                conversationId: samAndMerryConversation.Id
            );

            merryChatApp.SynchronizeData();
            samwiseChatApp.SynchronizeData();

            var samMessages2 = await samwiseChatApp.MessageFileService.GetMessages(samConvo.Id);
            Assert.IsTrue(samMessages2.Count() == 2);
            var latestMessageFromMerry = samMessages2.LastOrDefault();
            Assert.IsNotNull(latestMessageFromMerry);
            Assert.IsTrue(latestMessageFromMerry.Text == firstReplyFromMerry);
            await samwiseChatApp.MessageFileService.NotifyMessageWasRead(latestMessageFromMerry.ConversationId, latestMessageFromMerry.Id);


            //Validate sam received a reply
            var replyFromMerry = samMessages2.SingleOrDefault(x => x.Text == firstReplyFromMerry);
            Assert.IsNotNull(replyFromMerry);
            Assert.IsTrue(replyFromMerry.Sender == merryChatApp.Identity);

            // sam replies
            await samwiseChatApp.MessageFileService.SendMessage(
                id: Guid.NewGuid(),
                messageText: firstReplyFromSam,
                conversationId: samAndMerryConversation.Id
            );

            samwiseChatApp.SynchronizeData();
            merryChatApp.SynchronizeData();

            var merryMessages2 = await merryChatApp.MessageFileService.GetMessages(samAndMerryConversation.Id);
            var firstReceivedReplyFromSam = merryMessages2.SingleOrDefault(msg => msg.Text == firstReplyFromSam);
            Assert.IsNotNull(firstReceivedReplyFromSam);

            await merryChatApp.MessageFileService.NotifyMessageWasRead(firstReceivedReplyFromSam.ConversationId, firstReceivedReplyFromSam.Id);
            samwiseChatApp.SynchronizeData();
            merryChatApp.SynchronizeData();
            // System.Threading.Thread.Sleep(10000);
            await merryChatApp.MessageFileService.ReactToMessage(firstReceivedReplyFromSam.ConversationId, firstReceivedReplyFromSam.Id, merryReaction);

            samwiseChatApp.SynchronizeData();
            merryChatApp.SynchronizeData();

            // Assert.IsTrue(msgFromMerry.Reactions.Count() == 1);
            await RenderMessages(samwiseChatApp, samAndMerryConversation.Id);
            Console.WriteLine("\n===========\n");
            await RenderMessages(merryChatApp, samAndMerryConversation.Id);
        }

        private async Task RenderMessages(ChatApp app, Guid convoId)
        {
            Console.WriteLine($"\n\n{app.Identity} Messages\n===================================");

            var messages = await app.MessageFileService.GetMessages(convoId);
            foreach (var msg in messages)
            {
                var receivedDate = UnixTimeStampToDateTime(msg.ReceivedTimestamp);
                Console.WriteLine($"({receivedDate}) {msg.Sender} ⇶ \t{msg.Text}");

                foreach (var receipt in msg.ReadReceipts)
                {
                    Console.Write("\t");
                    Console.WriteLine($"{receipt.Sender} ˿ Read message at {UnixTimeStampToDateTime(receipt.Timestamp.milliseconds)}");
                }

                foreach (var msgReaction in msg.Reactions)
                {
                    Console.Write("\t");
                    Console.WriteLine($"{msgReaction.Sender} ˿ React with {msgReaction.ReactionValue}");
                }
            }
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStampMs)
        {
            // Unix timestamp is seconds past epoch
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddMilliseconds(unixTimeStampMs).ToLocalTime();
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