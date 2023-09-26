using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.Circles;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles
{
    public interface ICircleDefinitionOwnerClient
    {
        private const string RootPath = OwnerApiPathConstants.CirclesDefinitionsV1;

        [Get(RootPath + "/list")]
        Task<ApiResponse<IEnumerable<CircleDefinition>>> GetCircleDefinitions(bool includeSystemCircle = false);
        
        [Post(RootPath + "/get")]
        Task<ApiResponse<CircleDefinition>> GetCircleDefinition([Body] Guid id);

        [Post(RootPath + "/create")]
        Task<ApiResponse<bool>> CreateCircleDefinition([Body] CreateCircleRequest request);

        [Post(RootPath + "/update")]
        Task<ApiResponse<bool>> UpdateCircleDefinition([Body] CircleDefinition circleDefinition);

        [Post(RootPath + "/delete")]
        Task<ApiResponse<bool>> DeleteCircleDefinition([Body] Guid id);
        
        [Post(RootPath + "/disable")]
        Task<ApiResponse<bool>> DisableCircleDefinition([Body] Guid id);
        
        [Post(RootPath + "/enable")]
        Task<ApiResponse<bool>> EnableCircleDefinition([Body] Guid id);
    }
}