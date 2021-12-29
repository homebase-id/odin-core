using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Hosting.Tests.OwnerApi;
using Youverse.Hosting.Tests.OwnerApi.Authentication;

namespace Youverse.Hosting.Tests.AppAPI.Authentication
{
    [TestFixture]
    public class AppRevocationTests
    {
        private OwnerConsoleTestScaffold _ownerTestScaffold;
        private AppTestScaffold _appTestScaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _ownerTestScaffold = new OwnerConsoleTestScaffold(folder);
            _ownerTestScaffold.RunBeforeAnyTests();

            _appTestScaffold = new AppTestScaffold(folder);
            _appTestScaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _ownerTestScaffold.RunAfterAnyTests();
            _appTestScaffold.RunBeforeAnyTests();
        }

        [Test]
        public async Task AppCanBeRevoked()
        {
            Assert.Inconclusive("TODO");
        }
        
        [Test]
        public async Task DeviceCanBeRevoked()
        {
            Assert.Inconclusive("TODO");
        }
    }
}