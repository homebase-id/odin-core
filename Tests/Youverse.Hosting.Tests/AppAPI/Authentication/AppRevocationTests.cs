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