using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    public class OutboxTests
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

        // [Test(Description = "")]
        // public async Task CanMarkItemAsFailureAndIsCheckedIn()
        // {
        // }


        // [Test(Description = "")]
        // public async Task CanCheckoutItem()
        // {
        // }


        [Test(Description = "")]
        public async Task CanGetOutboxList()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() { TestIdentities.Frodo };
            var utilsContext = await _scaffold.TransferFile(sender, recipients);

            using (var client = _scaffold.CreateAppApiHttpClient(sender, utilsContext.AuthResult))
            {
                var svc = RestService.For<ITransitTestOutboxHttpClient>(client);

                var itemsResponse = await svc.GetOutboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;

                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count > 0); //TODO: need to actually check for an accurate count
            }
        }

        [Test(Description = "")]
        public async Task CanRemoveOutboxItem()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() { TestIdentities.Frodo };
            var utilsContext = await _scaffold.TransferFile(sender, recipients);

            using (var client = _scaffold.CreateAppApiHttpClient(sender, utilsContext.AuthResult))
            {
                var svc = RestService.For<ITransitTestOutboxHttpClient>(client);
                var itemsResponse = await svc.GetOutboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                var itemId = items.Results.First().Id;
                var removeItemResponse = await svc.RemoveOutboxItem(itemId);
                Assert.IsTrue(removeItemResponse.IsSuccessStatusCode);

                var getItemResponse = await svc.GetOutboxItem(itemId);

                Assert.IsTrue(getItemResponse.IsSuccessStatusCode);
                Assert.IsTrue(getItemResponse.Content == null);
            }
        }

        [Test(Description = "")]
        public async Task CanGetOutboxItem()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() { TestIdentities.Frodo };
            var utilsContext = await _scaffold.TransferFile(sender, recipients);

            using (var client = _scaffold.CreateAppApiHttpClient(sender, utilsContext.AuthResult))
            {
                var svc = RestService.For<ITransitTestOutboxHttpClient>(client);
                var itemsResponse = await svc.GetOutboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);
                Assert.IsTrue(items.Results.Count == 1);

                var singleItemResponse = await svc.GetOutboxItem(items.Results.First().Id);

                Assert.IsTrue(singleItemResponse.IsSuccessStatusCode);
                var singleItem = singleItemResponse.Content;
                Assert.IsNotNull(singleItem);
                Assert.IsTrue(singleItem.Id == items.Results.First().Id);
            }
        }

        [Test(Description = "")]
        public async Task CanUpdateOutboxItemPriority()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() { TestIdentities.Frodo };
            var utilsContext = await _scaffold.TransferFile(sender, recipients);

            using (var client = _scaffold.CreateAppApiHttpClient(sender, utilsContext.AuthResult))
            {
                var svc = RestService.For<ITransitTestOutboxHttpClient>(client);
                var itemsResponse = await svc.GetOutboxItems(1, 100);

                Assert.IsTrue(itemsResponse.IsSuccessStatusCode);
                var items = itemsResponse.Content;
                Assert.IsNotNull(items);

                var itemId = items.Results.First().Id;

                var updateResponse = await svc.UpdateOutboxItemPriority(itemId, 999);

                Assert.IsTrue(updateResponse.IsSuccessStatusCode);

                var singleItemResponse = await svc.GetOutboxItem(itemId);

                Assert.IsTrue(singleItemResponse.IsSuccessStatusCode);
                var singleItem = singleItemResponse.Content;
                Assert.IsNotNull(singleItem);
                Assert.IsTrue(singleItem.Id == items.Results.First().Id);
                Assert.IsTrue(singleItem.Priority == 999);
            }
        }
    }
}