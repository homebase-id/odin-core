using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Youverse.Hosting.Middleware;
using Youverse.Hosting.Middleware.Logging;
using Youverse.Hosting.Multitenant;

namespace Youverse.Hosting;

public static class Certificate
{
    public static void Map(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
    {
        app.UseLoggingMiddleware();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMultiTenancy();

        app.UseDefaultFiles();
        app.UseCertificateForwarding();
        app.UseStaticFiles();

        app.UseRouting();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
    }
}