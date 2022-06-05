using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Drive
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
        public async Task CanQueryByOneTag()
        {
            var identity = TestIdentities.Samwise;

            Guid tag = Guid.NewGuid();
            List<Guid> tags = new List<Guid>() { tag };

            var metadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AppData = new()
                {
                    Tags = tags,
                },
            };

            var uploadContext = await _scaffold.Upload(identity, metadata);

            using (var client = _scaffold.CreateAppApiHttpClient(identity, uploadContext.AuthenticationResult))
            {
                var svc = RestService.For<IDriveQueryClient>(client);

                var response = await svc.GetByTag(uploadContext.TestAppContext.DriveAlias, tag, false, 1, 100);

                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                Assert.IsTrue(page.Results.Count > 0);
                Assert.IsNotNull(page.Results.Single(item => item.Tags.Contains(tag)));
            }

            var appId = uploadContext.AppId;
            using (var ownerClient = _scaffold.CreateOwnerApiHttpClient(identity, out var _, appId))
            {
                var svc = RestService.For<IDriveQueryClient>(ownerClient);

                var response = await svc.GetByTag(uploadContext.TestAppContext.DriveAlias, tag, false, 1, 100);

                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                Assert.IsTrue(page.Results.Count > 0);
                Assert.IsNotNull(page.Results.Single(item => item.Tags.Contains(tag)));
            }
        }

        [Test]
        public async Task CanQueryDriveRecentItems()
        {
            var identity = TestIdentities.Samwise;

            var metadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" })
                }
            };

            var uploadContext = await _scaffold.Upload(identity, metadata);

            using (var client = _scaffold.CreateAppApiHttpClient(identity, uploadContext.AuthenticationResult))
            {
                var svc = RestService.For<IDriveQueryClient>(client);

                UInt64 maxDate = 0; //get everything ever modified
                var startCursor = Array.Empty<byte>();
                var qp = new QueryParams();
                var options = new ResultOptions();
                var response = await svc.GetRecent(uploadContext.TestAppContext.DriveAlias, maxDate, startCursor, qp, options);
                // var response = await svc.GetRecentlyCreatedItems(uploadContext.TestAppContext.DriveAlias, true, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var batch = response.Content;
                Assert.IsNotNull(batch);

                //TODO: what to test here?
                Assert.IsTrue(batch.SearchResults.Count() > 1);
                Assert.IsNotNull(batch.StartCursor);
                //TODO: test that star cursor is not zeros
                
            }
        }

        [Test]
        public async Task CanQueryDriveRecentItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;

            var uploadContext = await _scaffold.Upload(identity);


            using (var client = _scaffold.CreateAppApiHttpClient(identity, uploadContext.AuthenticationResult))
            {
                var svc = RestService.For<IDriveQueryClient>(client);

                var response = await svc.GetRecentlyCreatedItems(uploadContext.TestAppContext.DriveAlias, false, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                Assert.IsTrue(page.Results.Count > 0);
                Assert.IsTrue(page.Results.All(item => string.IsNullOrEmpty(item.JsonContent)), "One or more items had content");
            }
        }
    }
}