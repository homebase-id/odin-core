using System;
using Refit;
using System.Linq;
using NUnit.Framework;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    public class InboxTests
    {
        private TestScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new TestScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [TearDown]
        public void TearDown()
        {
        }

        [Test(Description = "")]
        public async Task CanGetInboxList()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() {TestIdentities.Frodo};
            var utilsContext = await _scaffold.TransferFile(sender, recipients, new TransitTestUtilsOptions() {ProcessOutbox = true, ProcessTransitBox = true});

            var recipient = utilsContext.RecipientContexts[TestIdentities.Frodo];
            Console.WriteLine($"Identity: {recipient.Identity}\nAppId: {recipient.AppId}, Token:{recipient.AuthResult.SessionToken}");
            using (var client = _scaffold.CreateAppApiHttpClient(TestIdentities.Frodo, recipient.AuthResult))
            {
                var svc = RestService.For<ITransitTestAppHttpClient>(client);
                var itemsResponse = await svc.GetInboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count > 0); //TODO: need to actually check for an exact count
            }
        }

        [Test(Description = "")]
        public async Task CanRemoveInboxItem()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() {TestIdentities.Frodo};
            var utilsContext = await _scaffold.TransferFile(sender, recipients, new TransitTestUtilsOptions() {ProcessOutbox = true, ProcessTransitBox = true});

            var recipient = utilsContext.RecipientContexts[TestIdentities.Frodo];
            Console.WriteLine($"Identity: {recipient.Identity}\nAppId: {recipient.AppId}, Token:{recipient.AuthResult.SessionToken}");
            using (var client = _scaffold.CreateAppApiHttpClient(TestIdentities.Frodo, recipient.AuthResult))
            {
                var svc = RestService.For<ITransitTestAppHttpClient>(client);
                var itemsResponse = await svc.GetInboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                var itemId = items.Results.First().Id;
                var removeItemResponse = await svc.RemoveInboxItem(itemId);
                Assert.IsTrue(removeItemResponse.IsSuccessStatusCode);

                var getItemResponse = await svc.GetInboxItem(itemId);

                Assert.IsTrue(getItemResponse.IsSuccessStatusCode);
                Assert.IsTrue(getItemResponse.Content == null);
            }
        }

        [Test(Description = "")]
        public async Task CanGetInboxItem()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() {TestIdentities.Frodo};
            var utilsContext = await _scaffold.TransferFile(sender, recipients, new TransitTestUtilsOptions() {ProcessOutbox = true, ProcessTransitBox = true});

            var recipient = utilsContext.RecipientContexts[TestIdentities.Frodo];
            Console.WriteLine($"Identity: {recipient.Identity}\nAppId: {recipient.AppId}, Token:{recipient.AuthResult.SessionToken}");
            using (var client = _scaffold.CreateAppApiHttpClient(TestIdentities.Frodo, recipient.AuthResult))
            {
                var svc = RestService.For<ITransitTestAppHttpClient>(client);
                var itemsResponse = await svc.GetInboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count == 1);

                var singleItemResponse = await svc.GetInboxItem(items.Results.First().Id);

                Assert.IsTrue(singleItemResponse.IsSuccessStatusCode);
                var singleItem = singleItemResponse.Content;
                Assert.IsNotNull(singleItem);
                Assert.IsTrue(singleItem.Id == items.Results.First().Id);
            }
        }
    }
}