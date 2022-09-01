﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Definition;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Circle
{
    public interface ICircleDefinitionOwnerClient
    {
        private const string RootPath = OwnerApiPathConstants.CirclesV1 + "/definitions";

        [Get(RootPath + "/list")]
        Task<ApiResponse<IEnumerable<CircleDefinition>>> GetCircleDefinitions();


        [Post(RootPath + "/get")]
        Task<ApiResponse<bool>> GetCircleDefinition([Body] ByteArrayId id);

        [Post(RootPath + "/create")]
        Task<ApiResponse<bool>> CreateCircleDefinition([Body] CreateCircleRequest request);

        [Post(RootPath + "/update")]
        Task<ApiResponse<bool>> UpdateCircleDefinition([Body] CircleDefinition circleDefinition);

        [Post(RootPath + "/delete")]
        Task<ApiResponse<bool>> DeleteCircleDefinition([Body] Guid id);
    }
}