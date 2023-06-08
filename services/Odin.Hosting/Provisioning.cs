using System.IO;
using System.Net.Mime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Youverse.Hosting;

public static class Provisioning
{
    public static void Map(IApplicationBuilder provApp, IWebHostEnvironment env, ILogger<Startup> logger)
    {
        provApp.UseDefaultFiles();
        provApp.UseCertificateForwarding();
        provApp.UseStaticFiles();
        provApp.UseRouting();
        provApp.UseAuthentication();
        provApp.UseAuthorization();
        provApp.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        provApp.UseHttpsRedirection();
        
        if (env.IsDevelopment())
        {
            Log.Information("using development route for provisioning");
            provApp.UseSwagger();
            provApp.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Provisioning Api v1"));
            provApp.MapWhen(ctx => true,
                homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3003/"); }); });
        }
        else
        {
            provApp.MapWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"),
                app =>
                {
                    var path = Path.Combine(env.ContentRootPath, "client", "provisioning-app");

                    app.UseStaticFiles(new StaticFileOptions()
                    {
                        FileProvider = new PhysicalFileProvider(path),
                        RequestPath = ""
                    });

                    app.Run(async context =>
                    {
                        context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                        await context.Response.SendFileAsync(Path.Combine(path, "index.html"));
                    });
                });
        }
    }
}