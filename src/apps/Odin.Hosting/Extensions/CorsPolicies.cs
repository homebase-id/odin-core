using System;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Base;

namespace Odin.Hosting.Extensions;

public static class CorsPolicies
{
    public const string OdinUnifiedCorsPolicy = "OdinUnifiedCorsPolicy";

    public static IServiceCollection AddCorsPolicies(this IServiceCollection services)
    {
        //
        // PREFLIGHT REQUEST TESTING
        //
        // curl -v -X OPTIONS https://frodo.dotyou.cloud/api/owner/v1/youauth/authorize" \
        //   -H "Origin: https://sam.dotyou.cloud" \
        //   -H "Access-Control-Request-Method: GET" \
        //   -H "Access-Control-Request-Headers: Content-Type"
        //
        // You should see these response headers:
        //
        // Access-Control-Allow-Origin: https://sam.dotyou.cloud
        // Access-Control-Allow-Methods: GET, POST, OPTIONS, PATCH, PUT, DELETE
        // Access-Control-Allow-Headers: Content-Type, ...
        // Access-Control-Allow-Credentials: true
        // Access-Control-Max-Age: 86400
        //

        //
        // ACTUAL REQUEST TESTING
        //
        // curl -v -X GET "https://frodo.dotyou.cloud/api/owner/v1/youauth/authorize" \
        //   -H "Origin: https://sam.dotyou.cloud"
        //
        // You should see:
        //
        // Access-Control-Allow-Origin: https://sam.dotyou.cloud
        // Access-Control-Allow-Credentials: true
        // Access-Control-Expose-Headers: your, custom, headers
        //

        services.AddCors(options =>
        {
            options.AddPolicy(OdinUnifiedCorsPolicy, policy =>
            {
                // SetIsOriginAllowed:
                // Access-Control-Allow-Origin
                // Tells the browser which origin is permitted to read the response.
                // By echoing back the request's Origin header, you're dynamically allowing whatever origin made the request.
                // This is the permissive approach — effectively "allow all origins" but done per-request.
                // If this header is missing or doesn't match the requesting origin,
                // the browser blocks the response from being accessed by JavaScript.
                // The request still happens server-side; CORS is a browser-enforced policy.
                policy.SetIsOriginAllowed(_ => true);

                // WithMethods:
                // Access-Control-Allow-Methods
                // Only relevant for preflight requests. Specifies which HTTP methods are permitted for the actual request.
                // Simple methods (GET, HEAD, POST) don't strictly require this, but PUT, PATCH, DELETE do.
                // Including all of them explicitly is standard practice.
                policy.WithMethods("GET", "POST", "OPTIONS", "PATCH", "PUT", "DELETE");

                // WithHeaders:
                // Access-Control-Allow-Headers
                // Only relevant for preflight requests (OPTIONS). Specifies which request headers the client is
                // allowed to send on the actual request.
                // Browsers send a preflight when the request uses headers outside the "CORS-safelisted" set.
                // Safelisted headers include Accept, Accept-Language, Content-Language, and Content-Type
                // (but only with simple values like application/x-www-form-urlencoded, multipart/form-data, text/plain).
                // Your custom headers and Content-Type: application/json
                // require explicit allowlisting here, otherwise the browser aborts before sending the actual request.
                policy.WithHeaders(
                    [
                        "Authorization",
                        "Content-Type",
                        ..OdinHeaderNames.CorsAllowedAndExposedHeaders
                    ]
                );

                // AllowCredentials:
                // Access-Control-Allow-Credentials
                // Tells the browser it's allowed to include credentials (cookies, HTTP auth, client-side certificates)
                // in cross-origin requests, and that the response can be exposed to JavaScript when credentials were sent.
                // Without this, even if cookies are sent, the browser hides the response from JS.
                // This also imposes a restriction: Access-Control-Allow-Origin cannot be * when credentials are allowed
                // - hence why you echo the specific origin.
                policy.AllowCredentials();

                // WithExposedHeaders:
                // Access-Control-Expose-Headers
                // Relevant for actual requests (not preflight). By default, JavaScript can only access these response headers:
                // Cache-Control, Content-Language, Content-Length, Content-Type, Expires, Last-Modified, Pragma.
                // This header lists additional response headers that JavaScript is allowed to read.
                // The * wildcard is supposed to mean "all headers," but per the spec, wildcard is ignored when credentials are enabled.
                // If you have custom response headers your client JS needs to read, enumerate them here.
                policy.WithExposedHeaders(OdinHeaderNames.CorsAllowedAndExposedHeaders);

                // SetPreflightMaxAge:
                // Access-Control-Max-Age
                // Only relevant for preflight requests. Tells the browser how long (in seconds) to cache the preflight response.
                // Without this, browsers may send a preflight before every single non-simple request, adding latency.
                // Note: browsers cap this value. Chrome caps at 7200 (2 hours), Firefox at 86400.
                policy.SetPreflightMaxAge(TimeSpan.FromSeconds(86400));
            });
        });

        return services;
    }
}
