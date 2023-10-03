using System.IO;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Odin.Core.Exceptions;

namespace Odin.Hosting.Controllers.Admin;

public class LocalhostWithApiKeyAttribute : ActionFilterAttribute
{
    private readonly bool _apiEnabled;
    private readonly string _apiKey;
    private readonly string _apiKeyHttpHeaderName;

    //

    public LocalhostWithApiKeyAttribute(bool apiEnabled, string apiKey, string apiKeyHttpHeaderName)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new OdinSystemException("Empty apiKey");
        }
        if (string.IsNullOrEmpty(apiKeyHttpHeaderName))
        {
            throw new OdinSystemException("Empty apiKeyHttpHeaderName");
        }
        _apiEnabled = apiEnabled;
        _apiKey = apiKey;
        _apiKeyHttpHeaderName = apiKeyHttpHeaderName;
    }

    //

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var remoteIp = context.HttpContext.Connection.RemoteIpAddress;
        if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
        {
            // Return 404 so we don't inform bad people that there could be something interesting here
            context.Result = new StatusCodeResult((int)HttpStatusCode.NotFound);
            return;
        }

        if (!_apiEnabled)
        {
            context.Result = new StatusCodeResult((int)HttpStatusCode.Conflict);
            return;
        }

        var apiKey = context.HttpContext.Request.Headers[_apiKeyHttpHeaderName].FirstOrDefault();
        if (apiKey != _apiKey)
        {
            context.Result = new StatusCodeResult((int)HttpStatusCode.Unauthorized);
            return;
        }

        base.OnActionExecuting(context);
    }

    //
}
