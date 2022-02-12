using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Tests.AppAPI.Drive;

namespace Youverse.Hosting.Tests.AnonymousApi.Drive
{
    public class DriveQueryTests
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

        [Test]
        public async Task FailsWhenNoValidIndex()
        {
            Assert.Inconclusive("TODO");
        }

        // [Test]
        // public async Task CanQueryDriveByCategory()
        // {
        // }
        //
        // [Test]
        // public async Task CanQueryDriveByCategoryNoContent()
        // {
        // }

        [Test]
        public async Task CanQueryDriveRecentItems()
        {
            var identity = TestIdentities.Samwise;

            var metadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AppData = new()
                {
                    Tags = new List<Guid>() {Guid.NewGuid()},
                    ContentIsComplete = true,
                    JsonContent = JsonConvert.SerializeObject(new {message = "We're going to the beach; this is encrypted by the app"})
                }
            };

            var uploadContext = await _scaffold.Upload(identity, metadata);

            using (var client = _scaffold.CreateAppApiHttpClient(identity, uploadContext.AuthResult))
            {
                var svc = RestService.For<IDriveQueryClient>(client);

                var response = await svc.GetRecentlyCreatedItems(true, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                //TODO: what to test here?
                Assert.IsTrue(page.Results.Count > 0);
            }
        }

        [Test]
        public async Task CanQueryDriveRecentItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;

            var uploadContext = await _scaffold.Upload(identity);

            using (var client = _scaffold.CreateAppApiHttpClient(identity, uploadContext.AuthResult))
            {
                var svc = RestService.For<IDriveQueryClient>(client);

                var response = await svc.GetRecentlyCreatedItems(false, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                Assert.IsTrue(page.Results.Count > 0);
                Assert.IsTrue(page.Results.All(item => string.IsNullOrEmpty(item.JsonContent)), "One or more items had content");
            }
        }
    }
}