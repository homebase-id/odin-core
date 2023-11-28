﻿#nullable enable
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Registry;

namespace Odin.Hosting.Multitenant
{
    internal class MultiTenantContainerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MultiTenantContainerMiddleware> _logger;
        private readonly MultiTenantContainerDisposableAccessor _container;
        private readonly IIdentityRegistry _identityRegistry;

        //

        public MultiTenantContainerMiddleware(
            RequestDelegate next,
            ILogger<MultiTenantContainerMiddleware> logger,
            MultiTenantContainerDisposableAccessor container,
            IIdentityRegistry identityRegistry)
        {
            _next = next;
            _logger = logger;
            _container = container;
            _identityRegistry = identityRegistry;
        }
        
        //

        public async Task Invoke(HttpContext context)
        {
            // Bail if we don't know the hostname/tenant
            var host = context.Request.Host.Host;
            var registration = _identityRegistry.ResolveIdentityRegistration(host, out _);
            if (registration == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync($"{host} not found");
                return;
            }

            if (registration.Disabled)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsync($"{host} is disabled");
                return;
            }
            
            // Begin new scope for request as ASP.NET Core standard scope is per-request
            var scope = _container.ContainerAccessor().GetCurrentTenantScope().BeginLifetimeScope("requestscope");
            context.RequestServices = new AutofacServiceProvider(scope);
            
            await _next(context);
        }
    }
}
