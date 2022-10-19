using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
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
            // var context = new ChatApp(_scaffold, TestIdentities.All);
            // await context.Initialize();
            //
            // var api = new ChatMessageApi(context);
            // await api.SendChatMessage(sender: TestIdentities.Frodo, recipient: TestIdentities.Samwise, "Let's roll kato");
            //
            // //TODO
            // string prevCursorState = "";
            // var (frodoSentMessages, frodoCursorState) = await api.GetMessages(TestIdentities.Frodo, prevCursorState);
            // var (samwiseReceivedMessages, samwiseCursorState) = await api.GetMessages(TestIdentities.Samwise, prevCursorState);
            //
            // //CollectionAssert.AreEquivalent(frodoSentMessages.ToList(), samwiseReceivedMessages.ToList());
        }

        [Test]
        public async Task CreateGroupAndSendMessage()
        {
            //Scenarios: 
            //create group
            // potential issues: when i am added to a group and I'm not connected to everyone in the group
            // 
            // send message to group
            //leave group
            //remove someone from group

            //send reaction
            //send delivered notification
            //send read notification
            //send reply

            var chatApps = await this.InitializeApps();
            
            var frodoChatApp = chatApps[TestIdentities.Frodo.DotYouId];
            var samwiseChatApp = chatApps[TestIdentities.Samwise.DotYouId];
            var merryChatApp = chatApps[TestIdentities.Merry.DotYouId];
            var pippinChat = chatApps[TestIdentities.Pippin.DotYouId];

            // Frodo sends a command to create a group
            var hobbitsChatGroup = new ChatGroup()
            {
                Id = Guid.NewGuid(),
                AdminDotYouId = frodoChatApp.Identity,
                Title = "le hobbits",
                Members = TestIdentities.All.Values.Select(k => k.DotYouId.ToString()).OrderBy(x => x).ToList()
            };

            await frodoChatApp.ConversationDefinitionService.CreateGroup(hobbitsChatGroup);

            await SynchronizeData(chatApps);

            System.Threading.Thread.Sleep(2000);

            // everyone should have the group
            foreach (var member in hobbitsChatGroup.Members)
            {
                var groups = chatApps[member].MessageService.GetGroups();
                var group = groups.SingleOrDefault(g => g.Id == hobbitsChatGroup.Id);

                Assert.IsNotNull(group);
                Assert.IsTrue(group.Id == hobbitsChatGroup.Id, $"Id did not match for {member}");
                Assert.IsTrue(group.Title == hobbitsChatGroup.Title, $"Title did not match for {member}");
                Assert.IsTrue(group.AdminDotYouId == hobbitsChatGroup.AdminDotYouId, $"Admin did not match for {member}");
                CollectionAssert.AreEquivalent(group.Members, hobbitsChatGroup.Members, $"member list did not match for {member}");
            }

            //
            var messageFromFrodoId = Guid.NewGuid();
            await frodoChatApp.MessageService.SendMessage(new ChatMessage()
            {
                Id = messageFromFrodoId,
                GroupId = hobbitsChatGroup.Id,
                Message = "south farthing time, anyone ;)?"
            });

            System.Threading.Thread.Sleep(2000);

            //issue: frodo does not have the conversation

            // await frodoChatApp.MessageService.React(hobbitsChatGroup.Id, messageFromFrodoId, "😀");
            await merryChatApp.MessageService.React(hobbitsChatGroup.Id, messageFromFrodoId, "😈");

            System.Threading.Thread.Sleep(2000);

            await SynchronizeData(chatApps);
            System.Threading.Thread.Sleep(2000);

            //now everyone should have the message
            foreach (var (identity, app) in chatApps)
            {
                await RenderMessages(app, hobbitsChatGroup.Id);
            }

            //CollectionAssert.AreEquivalent(frodoSentMessages.ToList(), samwiseReceivedMessages.ToList());
            Assert.Pass();
        }

        private async Task RenderMessages(ChatApp app, Guid groupId)
        {
            Console.WriteLine($"\n\n{app.Identity} Messages");

            var messages = await app.MessageService.GetMessages(groupId);
            foreach (var msg in messages)
            {
                var receivedDate = UnixTimeStampToDateTime(msg.ReceivedTimestamp);
                Console.WriteLine($"({receivedDate}) {msg.Message.Sender}:\t{msg.Message.Message}");

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