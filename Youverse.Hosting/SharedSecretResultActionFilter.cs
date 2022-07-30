using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Youverse.Hosting;

public class SharedSecretResultActionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        //no-op
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        //convert to encrypted data
        // context.HttpContext.Response.Clear();
        int i = 0;
    }
}