using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Base;
using Odin.Services.Membership.YouAuth;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;

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
        List<GuidId> circleIds = null,
        ConsentRequirementType consentRequirement = ConsentRequirementType.Never,
        UnixTimeUtc consentExpiration = default)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);

            var request = new YouAuthDomainRegistrationRequest()
            {
                Name = $"Test_{domain.DomainName}",
                Domain = domain.DomainName,
                CircleIds = circleIds ?? new List<GuidId>(),
                ConsentRequirements = new ConsentRequirements()
                {
                    ConsentRequirementType = consentRequirement,
                    Expiration = consentExpiration
                }
            };

            var response = await svc.RegisterDomain(request);

            return response;
        }
    }

    public async Task<ApiResponse<RedactedYouAuthDomainRegistration>> GetDomainRegistration(AsciiDomainName domain)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);
            var response = await svc.GetRegisteredDomain(new GetYouAuthDomainRequest() { Domain = domain.DomainName });
            // ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Could not retrieve the domain reg for {domain.DomainName}");
            return response;
        }
    }

    public async Task<ApiResponse<List<RedactedYouAuthDomainRegistration>>> GetDomains()
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);
            var response = await svc.GetRegisteredDomains();
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteDomainRegistration(AsciiDomainName domain)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);

            return await svc.DeleteDomain(new GetYouAuthDomainRequest()
            {
                Domain = domain.DomainName
            });
        }
    }

    public async Task<ApiResponse<HttpContent>> RevokeDomain(AsciiDomainName domain)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);
            return await svc.RevokeDomain(new GetYouAuthDomainRequest()
            {
                Domain = domain.DomainName
            });
        }
    }

    public async Task<ApiResponse<HttpContent>> AllowDomain(AsciiDomainName domain)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);
            return await svc.RemoveDomainRevocation(new GetYouAuthDomainRequest()
            {
                Domain = domain.DomainName
            });
        }
    }

    public async Task<ApiResponse<HttpContent>> GrantCircle(AsciiDomainName domain, GuidId circleId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);

            return await svc.GrantCircle(new GrantYouAuthDomainCircleRequest()
            {
                Domain = domain.DomainName,
                CircleId = circleId
            });
        }
    }

    public async Task<ApiResponse<HttpContent>> RevokeCircle(AsciiDomainName domain, GuidId circleId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);

            return await svc.RevokeCircle(new RevokeYouAuthDomainCircleRequest()
            {
                Domain = domain.DomainName,
                CircleId = circleId
            });
        }
    }

    public async Task<ApiResponse<List<RedactedYouAuthDomainClient>>> GetRegisteredClients(AsciiDomainName domain)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);
            return await svc.GetRegisteredClients(domain.DomainName);
        }
    }

    public async Task<ApiResponse<YouAuthDomainClientRegistrationResponse>> RegisterClient(AsciiDomainName domain, string friendlyName)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);
            return await svc.RegisterClient(new YouAuthDomainClientRegistrationRequest()
            {
                Domain = domain.DomainName,
                ClientFriendlyName = friendlyName
            });
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteClient(Guid accessRegistrationId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ownerSharedSecret);
            return await svc.DeleteClient(new GetYouAuthDomainClientRequest()
            {
                AccessRegistrationId = accessRegistrationId
            });
        }
    }
}