#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Util;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Base;
using Odin.Services.Membership.YouAuth;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

public sealed partial class OwnerAdmin
{
    /// <summary>
    /// Registers a YouAuth domain (the third-party domain that a Guest test caller represents),
    /// optionally granting it the given circles.
    /// </summary>
    public async Task<ApiResponse<RedactedYouAuthDomainRegistration>> RegisterYouAuthDomain(
        AsciiDomainName domain,
        List<GuidId>? circleIds = null)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ss);
        var response = await svc.RegisterDomain(new YouAuthDomainRegistrationRequest
        {
            Name = $"Test_{domain.DomainName}",
            Domain = domain.DomainName,
            CircleIds = circleIds ?? new List<GuidId>(),
            ConsentRequirements = new ConsentRequirements { ConsentRequirementType = ConsentRequirementType.Never }
        });
        EnsureSuccess(response, nameof(RegisterYouAuthDomain));
        return response;
    }

    /// <summary>
    /// Registers a client under an already-registered YouAuth domain. The returned access token
    /// and shared secret are what the <see cref="GuestSession"/> uses to authenticate.
    /// </summary>
    public async Task<ApiResponse<YouAuthDomainClientRegistrationResponse>> RegisterYouAuthClient(
        AsciiDomainName domain,
        string friendlyName = "test in-process guest client")
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ss);
        var response = await svc.RegisterClient(new YouAuthDomainClientRegistrationRequest
        {
            Domain = domain.DomainName,
            ClientFriendlyName = friendlyName
        });
        EnsureSuccess(response, nameof(RegisterYouAuthClient));
        return response;
    }
}
