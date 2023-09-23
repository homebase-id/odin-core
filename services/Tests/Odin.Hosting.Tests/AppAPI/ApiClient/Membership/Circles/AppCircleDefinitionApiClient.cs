using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Circles;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests.AppAPI.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Circles;

public class AppCircleDefinitionApiClient : AppApiTestUtils
{
    private readonly AppClientToken _token;

    public AppCircleDefinitionApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }
    
    public async Task<ApiResponse<CircleDefinition>> GetCircleDefinition(Guid circleId)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<IAppCircleDefinitionClient>(client,  _token.SharedSecret);
            var response = await svc.GetCircleDefinition(circleId);
            return response;
        }
    }
    
    public async Task<ApiResponse<List<CircleDefinition>>> GetCircleDefinitions(bool includeSystemCircle = false)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<IAppCircleDefinitionClient>(client,  _token.SharedSecret);
            var response = await svc.GetCircleDefinitions();
            return response;
        }
    }
    
    public async Task<ApiResponse<bool>> Update(CircleDefinition definition)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<IAppCircleDefinitionClient>(client,  _token.SharedSecret);
            var response = await svc.UpdateCircleDefinition(definition);
            return response;
        }
    }
    
    public async Task<ApiResponse<bool>> Delete(Guid id)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<IAppCircleDefinitionClient>(client,  _token.SharedSecret);
            var response = await svc.DeleteCircleDefinition(id);
            return response;
        }
    }
    
    public async Task<ApiResponse<bool>> Create(CreateCircleRequest request)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<IAppCircleDefinitionClient>(client,  _token.SharedSecret);
            var response = await svc.CreateCircleDefinition(request);
            return response;
        }
    }
    
    public async Task<ApiResponse<bool>> Disable(Guid id)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<IAppCircleDefinitionClient>(client,  _token.SharedSecret);
            var response = await svc.DisableCircleDefinition(id);
            return response;
        }
    }
    
    public async Task<ApiResponse<bool>> Enable(Guid id)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<IAppCircleDefinitionClient>(client,  _token.SharedSecret);
            var response = await svc.EnableCircleDefinition(id);
            return response;
        }
    }
}