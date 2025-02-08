using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Hosting;

namespace Odin.Hosting.Middleware
{
    public class LinkPreviewMiddleware(RequestDelegate next)
    {
        //Note: be sure it does not end with a "/"
        private static readonly List<string> pathsToHandle = ["/posts"];

        public bool IsBrowser(HttpContext httpContext)
        {
            string userAgent = httpContext.Request.Headers["User-Agent"].ToString().ToLower();

            if (userAgent.Contains("chrome") && !userAgent.Contains("edg"))
                return true;

            if (userAgent.Contains("edg"))
                return true;

            if (userAgent.Contains("firefox"))
                return true;

            if (userAgent.Contains("safari") && !userAgent.Contains("chrome"))
                return true;

            if (userAgent.Contains("opera") || userAgent.Contains("opr"))
                return true;

            return false;
        }

        public async Task Invoke(HttpContext httpContext, IHostEnvironment env)
        {
            if (IsBrowser(httpContext))
            {
                await next(httpContext);
                return;
            }

            if (!pathsToHandle.Any(s => httpContext.Request.Path.StartsWithSegments(s)))
            {
                await next(httpContext);
                return;
            }


            // get the unique id
            var uidText = httpContext.Request.Path.Value?.Split('/').LastOrDefault();

            if (null == uidText)
            {
                await next(httpContext);
                return;
            }

            if (!Guid.TryParse(uidText, out var uid))
            {
                await next(httpContext);
                return;
            }

            
            await httpContext.Response.WriteAsync($"""
                                                  <head>
                                                      <meta property="og:title" content="The post id is {uid.ToString()}" />
                                                      <meta property="og:description" content="I can see you're not a browser so here's a picture of an apple" />
                                                      <meta property="og:image" content="https://unsplash.com/photos/one-red-apple-CoqJGsFVJtM" />
                                                      <meta property="og:url" content="{httpContext.Request.GetDisplayUrl()}" />
                                                      <meta property="og:type" content="website" />
                                                  </head>
                                                  """);
            // look up the file
            // get the 'link-prevw' payload

            // send processing
        }
    }
}