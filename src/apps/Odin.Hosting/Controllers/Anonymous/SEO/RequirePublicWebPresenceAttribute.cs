using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Base;
using Odin.Services.PublicPage;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

/// <summary>
/// Short-circuits public-page endpoints with a plain placeholder page when the
/// tenant is not allowed a public web presence.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePublicWebPresenceAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenantContext = context.HttpContext.RequestServices.GetRequiredService<TenantContext>();
        if (!tenantContext.EnablePublicWebPresence)
        {
            var response = context.HttpContext.Response;
            response.ContentType = MediaTypeNames.Text.Html;
            await response.WriteAsync(HomebasePublicPageService.NoWebPresenceHtml);
            context.Result = new EmptyResult();
            return;
        }

        await next();
    }
}
