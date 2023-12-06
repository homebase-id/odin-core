using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.Circles;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.CircleMembership
{
    public interface IRefitOwnerCircleDefinition
    {
        private const string RootPath = OwnerApiPathConstants.CirclesDefinitionsV1;

        [Get(RootPath + "/list")]
        Task<ApiResponse<IEnumerable<CircleDefinition>>> GetCircleDefinitions(bool includeSystemCircle = false);
        
        [Post(RootPath + "/get")]
        Task<ApiResponse<CircleDefinition>> GetCircleDefinition([Body] Guid id);

        [Post(RootPath + "/create")]
        Task<ApiResponse<HttpContent>> CreateCircleDefinition([Body] CreateCircleRequest request);

        [Post(RootPath + "/update")]
        Task<ApiResponse<HttpContent>> UpdateCircleDefinition([Body] CircleDefinition circleDefinition);

        [Post(RootPath + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteCircleDefinition([Body] Guid id);
        
        [Post(RootPath + "/disable")]
        Task<ApiResponse<HttpContent>> DisableCircleDefinition([Body] Guid id);
        
        [Post(RootPath + "/enable")]
        Task<ApiResponse<HttpContent>> EnableCircleDefinition([Body] Guid id);
    }
}