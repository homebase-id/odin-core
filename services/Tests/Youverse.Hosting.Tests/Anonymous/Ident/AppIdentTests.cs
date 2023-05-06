using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Tests.Anonymous.Ident
{
    [TestFixture]
    public class AppIdentTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanGetIdentInfo()
        {
            var anonClient = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Samwise.OdinId);

            var svc = RestService.For<IIdentHttpClient>(anonClient);

            var identResponse = await svc.GetIdent();
            var ident = identResponse.Content;
            Assert.IsFalse(string.IsNullOrEmpty(ident.OdinId));
            Assert.IsTrue(ident.Version == 1.0);
        }
    }
}