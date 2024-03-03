﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Odin.Core.Services.Base
{
    /// <summary>
    /// Contains all information required to execute commands in the Odin.Core.Services services.
    /// </summary>
    public class OdinContextAccessor
    {
        private readonly IHttpContextAccessor _accessor;

        public OdinContextAccessor(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public OdinContext GetCurrent()
        {
            return _accessor.HttpContext.RequestServices.GetRequiredService<OdinContext>();
        }
    }
}