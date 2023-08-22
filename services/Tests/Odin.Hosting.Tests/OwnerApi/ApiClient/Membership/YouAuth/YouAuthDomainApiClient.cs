using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.YouAuth;
using Odin.Core.Util;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.YouAuth;
using Odin.Hosting.Tests.OwnerApi.Utils;
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
        AsciiDomainName domain, List<GuidId> circleIds = null)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IYouAuthDomainRegistrationClient>(client, ownerSharedSecret);

            var request = new YouAuthDomainRegistrationRequest()
            {
                Name = $"Test_{domain.DomainName}",
                Domain = domain.DomainName,
                CircleIds = circleIds ?? new List<GuidId>()
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

    public async Task<ApiResponse<bool>> GrantCircle(AsciiDomainName domain, GuidId circleId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IYouAuthDomainRegistrationClient>(client, ownerSharedSecret);

            return await svc.GrantCircle(new GrantYouAuthDomainCircleRequest()
            {
                Domain = domain.DomainName,
                CircleId = circleId
            });
        }
    }

    public async Task<ApiResponse<bool>> RevokeCircle(AsciiDomainName domain, GuidId circleId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IYouAuthDomainRegistrationClient>(client, ownerSharedSecret);

            return await svc.RevokeCircle(new RevokeYouAuthDomainCircleRequest()
            {
                Domain = domain.DomainName,
                CircleId = circleId
            });
        }
    }
}