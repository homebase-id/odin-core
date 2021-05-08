using DotYou.Types;
using NUnit.Framework;
using Refit;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class AuthorizationTests
    {
        private TestScaffold scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            scaffold = new TestScaffold(folder);
            scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            scaffold.RunAfterAnyTests();
        }

        [SetUp]
        public void Setup() { }

        [Test]
        public async Task CannotPerformUnauthorizedAction()
        {
            //have sam attempted to perform an action on frodos site
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                //point sams client to frodo
                client.BaseAddress = new Uri($"https://{scaffold.Frodo}");
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden, "User was able to perform unauthorized action");

            }
        }

        [Test]
        public async Task CanSuccessfullyPerformAuthorized()
        {
            //have sam perform a normal operation on his site
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden, "User was able to perform unauthorized action");

            }
        }
        
    }
}