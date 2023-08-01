using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.OpenApi.Validations.Rules;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.YouAuth;
using Odin.Core.Services.Base;
using Odin.Core.Util;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuthDomainManagement;
using Odin.Hosting.Tests.OwnerApi.Apps;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Hosting.Tests.OwnerApi.YouAuthDomains;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient;

public class YouAuthDomainApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public YouAuthDomainApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }


    public async Task<ApiResponse<RedactedYouAuthDomainRegistration>> RegisterDomain(
        AsciiDomainName domain,
        PermissionSetGrantRequest permissions)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IYouAuthDomainRegistrationClient>(client, ownerSharedSecret);

            var request = new YouAuthDomainRegistrationRequest()
            {
                Name = $"Test_{domain.DomainName}",
                Domain = domain.DomainName,
                PermissionSet = permissions.PermissionSet,
                Drives = permissions.Drives?.ToList(),
            };

            var response = await svc.RegisterDomain(request);

            return response;
        }
    }

    public async Task<ApiResponse<RedactedYouAuthDomainRegistration>> GetDomainRegistration(AsciiDomainName domain)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IYouAuthDomainRegistrationClient>(client, ownerSharedSecret);
            var response = await svc.GetRegisteredDomain(new GetYouAuthDomainRequest() { Domain = domain.DomainName });
            // Assert.IsTrue(response.IsSuccessStatusCode, $"Could not retrieve the domain reg for {domain.DomainName}");
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> UpdatePermissions(AsciiDomainName domain, PermissionSetGrantRequest grant)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IYouAuthDomainRegistrationClient>(client, ownerSharedSecret);

            return await svc.UpdatePermissions(new UpdateYouAuthDomainPermissionsRequest()
            {
                Domain = domain,
                Drives = grant.Drives,
                PermissionSet = grant.PermissionSet
            });
        }
    }
}