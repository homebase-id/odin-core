using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Circles;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Circles
{
    public interface IAppCircleDefinitionClient
    {
        private const string RootPath = AppApiPathConstantsV1.CirclesDefinitionsV1;

        [Get(RootPath + "/list")]
        Task<ApiResponse<List<CircleDefinition>>> GetCircleDefinitions(bool includeSystemCircle = false);
        
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