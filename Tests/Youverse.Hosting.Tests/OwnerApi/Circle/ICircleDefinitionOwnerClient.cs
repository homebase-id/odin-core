using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Definition;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Circle
{
    public interface ICircleDefinitionOwnerClient
    {
        private const string RootPath = OwnerApiPathConstants.CirclesV1 + "/definitions";

        [Get(RootPath)]
        Task<ApiResponse<IEnumerable<CircleDefinition>>> GetCircleDefinitions();

        [Post(RootPath)]
        Task<ApiResponse<HttpContent>> Create([Body] CreateCircleRequest request);

        [Put(RootPath)]
        Task<ApiResponse<HttpContent>> UpdateCircle([Body] CircleDefinition circleDefinition);

        [Delete(RootPath)]
        Task<ApiResponse<HttpContent>> DeleteCircle(Guid id);
    }
}