using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Hosting.Tests.ApiClient;

namespace Youverse.Hosting.Tests.Drive
{
    public class DriveQueryTests
    {
        private TestScaffold _scaffold;
        private readonly Guid _profileDriveId = ProfileIndexManager.DataAttributeDriveId;

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
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                try
                {
                    var svc = RestService.For<IOwnerDriveQueryClient>(client);
                    var response = await svc.GetRecentlyCreatedItems(_profileDriveId, true, 1, 100);
                }
                catch (ValidationApiException e)
                {
                    Assert.Pass("");
                    return;
                }
                catch (Exception e2)
                {
                    string x = "";
                }
                
                Assert.Fail();
            }
        }

        [Test]
        public async Task CanQueryDriveByCategory()
        {
        }

        [Test]
        public async Task CanQueryDriveByCategoryNoContent()
        {
        }

        [Test]
        public async Task CanQueryDriveRecentItems()
        {
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var indexMgmtSvc = RestService.For<IOwnerDriveIndexManagementClient>(client);
                var rebuildResponse = await indexMgmtSvc.Rebuild(_profileDriveId);

                //HACK: wait on index to be ready
                Thread.Sleep(2000);
                
                var svc = RestService.For<IOwnerDriveQueryClient>(client);

                var response = await svc.GetRecentlyCreatedItems(_profileDriveId, true, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                //TODO: what to test here?
                Assert.IsTrue(page.Results.Count > 0);
            }
        }

        [Test]
        public async Task CanQueryDriveRecentItemsNoContent()
        {
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var indexMgmtSvc = RestService.For<IOwnerDriveIndexManagementClient>(client);
                await indexMgmtSvc.Rebuild(_profileDriveId);

                var svc = RestService.For<IOwnerDriveQueryClient>(client);

                var response = await svc.GetRecentlyCreatedItems(_profileDriveId, false, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                Assert.IsTrue(page.Results.Count > 0);
                Assert.IsTrue(page.Results.All(item => string.IsNullOrEmpty(item.JsonPayload)), "One or more items had content");
            }
        }
    }
}