using System.IO;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Odin.Core.Exceptions;

namespace Odin.Hosting.Controllers.Admin;

public class AdminApiRestrictedAttribute : ActionFilterAttribute
{
    private readonly bool _apiEnabled;
    private readonly string _apiKey;
    private readonly string _apiKeyHttpHeaderName;
    private readonly int _apiPort;

    //

    public AdminApiRestrictedAttribute(bool apiEnabled, string apiKey, string apiKeyHttpHeaderName, int apiPort)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new OdinSystemException("Empty apiKey");
        }
        if (string.IsNullOrEmpty(apiKeyHttpHeaderName))
        {
            throw new OdinSystemException("Empty apiKeyHttpHeaderName");
        }
        if (apiPort < 1)
        {
            throw new OdinSystemException("Invalid apiPort");
        }
        _apiEnabled = apiEnabled;
        _apiKey = apiKey;
        _apiKeyHttpHeaderName = apiKeyHttpHeaderName;
        _apiPort = apiPort;
    }

    //

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.Connection.LocalPort != _apiPort)
        {
            // Return 404 so we don't inform bad people that there could be something interesting here
            // Strange hack to circumvent creation of a ProblemDetails
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.NotFound };
            return;
        }

        // var remoteIp = context.HttpContext.Connection.RemoteIpAddress;
        // if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
        // {
        //     // Return 404 so we don't inform bad people that there could be something interesting here
        //     context.Result = new StatusCodeResult((int)HttpStatusCode.NotFound);
        //     return;
        // }

        if (!_apiEnabled)
        {
            // Strange hack to circumvent creation of a ProblemDetails
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.Conflict };
            return;
        }

        var apiKey = context.HttpContext.Request.Headers[_apiKeyHttpHeaderName].FirstOrDefault();
        if (apiKey != _apiKey)
        {
            // Strange hack to circumvent creation of a ProblemDetails
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.Unauthorized };
            return;
        }

        base.OnActionExecuting(context);
    }

    //
}
