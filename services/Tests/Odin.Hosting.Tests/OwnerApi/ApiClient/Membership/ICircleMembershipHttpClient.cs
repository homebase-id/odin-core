using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership;
using Odin.Core.Services.Membership.Circles;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Membership
{
    public interface ICircleMembershipHttpClient
    {
        private const string RootPath = OwnerApiPathConstants.CirclesV1 + "/membership";

        [Post(RootPath + "/list")]
        Task<ApiResponse<List<CircleDomainResult>>> GetDomainsInCircle([Body] GetCircleMembersRequest request);
    }
}