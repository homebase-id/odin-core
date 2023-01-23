using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Controllers.OwnerToken.Drive;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.OwnerApi.Circle;
using Youverse.Hosting.Tests.OwnerApi.Configuration;
using Youverse.Hosting.Tests.OwnerApi.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Utils.Fluid
{
    public class OwnerApiClient
    {
        private readonly TestIdentity _identity;
        private readonly OwnerApiTestUtils _ownerApi;

        private readonly AppsApiClient _appsApiClient;
        private readonly CircleNetworkApiClient _circleNetworkApiClient;
        private readonly TransitApiClient _transitApiClient;
        private readonly DriveApiClient _driveApiClient;

        public OwnerApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            _appsApiClient = new AppsApiClient(ownerApi, identity);
            _circleNetworkApiClient = new CircleNetworkApiClient(ownerApi, identity);
            _transitApiClient = new TransitApiClient(ownerApi, identity);
            _driveApiClient = new DriveApiClient(ownerApi, identity);
        }

        public TestIdentity Identity => _identity;

        public AppsApiClient Apps => _appsApiClient;

        public CircleNetworkApiClient Network => _circleNetworkApiClient;

        public TransitApiClient Transit => _transitApiClient;
        
        public DriveApiClient Drive => _driveApiClient;

        public async Task InitializeIdentity(InitialSetupRequest setupConfig)
        {
            using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                var getIsIdentityConfiguredResponse = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
                Assert.IsTrue(getIsIdentityConfiguredResponse.Content);
            }
        }
    }
}