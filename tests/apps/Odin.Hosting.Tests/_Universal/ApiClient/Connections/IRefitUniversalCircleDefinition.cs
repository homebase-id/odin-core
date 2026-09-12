using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Circles;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Connections
{
    public interface IRefitUniversalCircleDefinition
    {
        private const string RootPath = "/circles/definitions";

        [Get(RootPath + "/list")]
        Task<ApiResponse<IEnumerable<CircleDefinition>>> GetCircleDefinitions(bool includeSystemCircle = false);
        
        [Post(RootPath + "/get")]
        Task<ApiResponse<CircleDefinition>> GetCircleDefinition([Body] Guid id);

        [Post(RootPath + "/create")]
        Task<ApiResponse<HttpContent>> CreateCircleDefinition([Body] CreateCircleRequest request);

        [Post(RootPath + "/reassign-owner")]
        Task<ApiResponse<HttpContent>> ReassignCircleOwningApp([Body] SetCircleOwningAppRequest request);

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