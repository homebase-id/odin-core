using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public class DriveQueryTests
    {
        private TestScaffold _scaffold;
        private readonly Guid _profileDriveId = Guid.Empty;

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
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var indexMgmtSvc = RestService.For<IOwnerDriveIndexManagementClient>(client);
                var rebuildResponse = await indexMgmtSvc.Rebuild(_profileDriveId);

                //HACK: wait on index to be ready
                Thread.Sleep(2000);
                
                var svc = RestService.For<IDriveQueryClient>(client);

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
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var indexMgmtSvc = RestService.For<IOwnerDriveIndexManagementClient>(client);
                await indexMgmtSvc.Rebuild(_profileDriveId);

                //HACK: wait on index to be ready
                Thread.Sleep(2000);
                
                var svc = RestService.For<IDriveQueryClient>(client);

                var response = await svc.GetRecentlyCreatedItems(_profileDriveId, false, 1, 100);
                Assert.IsTrue(response.IsSuccessStatusCode);
                var page = response.Content;
                Assert.IsNotNull(page);

                Assert.IsTrue(page.Results.Count > 0);
                Assert.IsTrue(page.Results.All(item => string.IsNullOrEmpty(item.JsonContent)), "One or more items had content");
            }
        }
    }
}