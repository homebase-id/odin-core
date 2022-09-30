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
            var chatApps = await this.InitializeApps();
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Samwise.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Merry.DotYouId);
            await _scaffold.OwnerApi.CreateConnection(TestIdentities.Frodo.DotYouId, TestIdentities.Pippin.DotYouId);

            var frodoChatApp = chatApps[TestIdentities.Frodo.DotYouId];
            var samwiseChatApp = chatApps[TestIdentities.Samwise.DotYouId];
            var merryChatApp = chatApps[TestIdentities.Merry.DotYouId];

            var hobbitsGroupMembers = TestIdentities.All.Values.Select(k => k.DotYouId.ToString()).OrderBy(x => x).ToList();
            var groupId = frodoChatApp.CommandService.CreateGroup("le hobbits", hobbitsGroupMembers);

            //see if the group exists on all member servers
            samwiseChatApp.CommandService.ProcessIncomingCommands();
            merryChatApp.CommandService.ProcessIncomingCommands();

            var samsGroups = samwiseChatApp.MessageService.GetGroups();
            var merrysGroups = merryChatApp.MessageService.GetGroups();

            //sam should have the group and merry should match
            Assert.IsNotNull(samsGroups.SingleOrDefault(g => g.Id == groupId));
            //CollectionAssert.AreEquivalent(samsGroups, merrysGroups);

            await frodoChatApp.MessageService.SendGroupMessage(
                groupId: groupId,
                message: new ChatMessage() { Message = "south farthing time, anyone ;)?" },
                recipients: hobbitsGroupMembers);

            var samMessages = await samwiseChatApp.MessageService.GetGroupMessages(groupId, "");
            var merryMessages = await merryChatApp.MessageService.GetGroupMessages(groupId, "");

            //TODO
            string frodoPrevCursorState = "";
            var (frodoSentMessages, frodoCursorState) = await frodoChatApp.MessageService.GetMessages(frodoPrevCursorState);
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
                var chatApp = new ChatApp((DotYouIdentity)dotYouId, testAppContext, _scaffold);
                apps.Add(dotYouId, chatApp);
            }

            return apps;
        }
    }
}