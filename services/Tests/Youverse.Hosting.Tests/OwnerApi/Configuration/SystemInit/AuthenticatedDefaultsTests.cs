using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Youverse.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class AuthenticatedDefaultsTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }


        [Test]
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public async Task CanAllowAuthenticatedVisitorsToViewConnections()
        {
            Assert.Inconclusive("TODO");
        }
        
        [Test]
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public async Task CanBlockAuthenticatedVisitorsFromViewingConnections()
        {
            Assert.Inconclusive("TODO");
        }
        

    }
}