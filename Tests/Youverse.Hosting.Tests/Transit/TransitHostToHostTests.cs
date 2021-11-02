using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Hosting.Controllers.Apps;
using Youverse.Hosting.Tests.Apps;

namespace Youverse.Hosting.Tests.Transit
{
    public class TransitHostToHostTests
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
        [Ignore("wip")]
        public async Task TransferDataToHost()
        {
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<ITransitTestHttpClient>(client);
                var response = await svc.SendHostToHost();
                
                Assert.IsTrue(response.IsSuccessStatusCode);
                
            }
        }
        
    }
}