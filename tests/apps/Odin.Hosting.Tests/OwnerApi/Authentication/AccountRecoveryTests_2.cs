using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Registry.Registration;
using Odin.Core.Time;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Authentication
{
    public class AccountRecoveryTests2
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);

            var dict = new Dictionary<string, string>()
            {
                { "Development__RecoveryKeyWaitingPeriodSeconds", "1" }
            };
            _scaffold.RunBeforeAnyTests(false, false, dict);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }


        [Test]
        public async Task FailToGetAccountRecoveryKeyOutsideOfTimeWindow()
        {
            var identity = TestIdentities.Merry;
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            
            Thread.Sleep(1000); //sleep to ensure 
            var response = await ownerClient.Security.GetAccountRecoveryKey();
            Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden, $"Status code was {response.StatusCode} but should have been Forbidden");
        }

    }
}