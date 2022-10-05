using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure.Design;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure
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
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Samwise.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Merry.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Pippin.DotYouId);

            var frodoChatApp = chatApps[TestIdentities.Frodo.DotYouId];
            var samwiseChatApp = chatApps[TestIdentities.Samwise.DotYouId];
            var merryChatApp = chatApps[TestIdentities.Merry.DotYouId];
            var pippinChat = chatApps[TestIdentities.Pippin.DotYouId];

            //TODO: create a connection between other parties; what happens when other parties are not connected but put into the same group?

            // Frodo sends a command to create a group
            var hobbitsChatGroup = new ChatGroup()
            {
                Id = Guid.NewGuid(),
                AdminDotYouId = frodoChatApp.Identity,
                Title = "le hobbits",
                Members = TestIdentities.All.Values.Select(k => k.DotYouId.ToString()).OrderBy(x => x).ToList()
            };

            await frodoChatApp.GroupDefinitionService.CreateGroup(hobbitsChatGroup);
            
            //Note: in this example, the group does not exist even for frodo until he processes the command
            // sync data for all members
            foreach (var member in hobbitsChatGroup.Members)
            {
                chatApps[member].SynchronizeData();
            }

            // everyone should have the group

            foreach (var member in hobbitsChatGroup.Members)
            {
                var groups = chatApps[member].MessageService.GetGroups();
                var group = groups.SingleOrDefault(g => g.Id == hobbitsChatGroup.Id);
                
                Assert.IsNotNull(group);
                Assert.IsTrue(group.Id == hobbitsChatGroup.Id, $"Id did not match for {member}");
                Assert.IsTrue(group.Title == hobbitsChatGroup.Title,$"Title did not match for {member}");
                Assert.IsTrue(group.AdminDotYouId == hobbitsChatGroup.AdminDotYouId,$"Admin did not match for {member}");
                CollectionAssert.AreEquivalent(group.Members, hobbitsChatGroup.Members,$"member list did not match for {member}");
            }
            
            //
            await frodoChatApp.MessageService.SendMessage(
                groupId: hobbitsChatGroup.Id,
                message: new ChatMessage() { Message = "south farthing time, anyone ;)?" });
            
            // var samMessages = await samwiseChatApp.MessageService.GetGroupMessages(groupId, "");
            // var merryMessages = await merryChatApp.MessageService.GetGroupMessages(groupId, "");


            //TODO
            // string frodoPrevCursorState = "";
            // var (frodoSentMessages, frodoCursorState) = await frodoChatApp.MessageService.GetMessages(frodoPrevCursorState);
            // string samPrevCursorState = "";
            // var (samwiseReceivedMessages, samwiseCursorState) = await samwiseChatApp.MessageService.GetMessages(prevCursorState);

            //CollectionAssert.AreEquivalent(frodoSentMessages.ToList(), samwiseReceivedMessages.ToList());
            Assert.Pass();
        }

        public async Task<Dictionary<string, ChatApp>> InitializeApps()
        {
            var apps = new Dictionary<string, ChatApp>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var (dotYouId, identity) in TestIdentities.All)
            {
                var testAppContext = await _scaffold.OwnerApi.SetupTestSampleApp(ChatApiConfig.AppId, identity, canReadConnections: true, ChatApiConfig.Drive, driveAllowAnonymousReads: false);
                var chatApp = new ChatApp(testAppContext, _scaffold);
                apps.Add(dotYouId, chatApp);
            }

            return apps;
        }
    }
}