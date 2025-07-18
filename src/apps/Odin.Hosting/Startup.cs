using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Tasks;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;
using Odin.Hosting._dev;
using Odin.Hosting.Middleware;
using Odin.Hosting.Middleware.Logging;
using Odin.Hosting.Multitenant;
using Odin.Services.Background;
using Odin.Services.LinkPreview;

namespace Odin.Hosting;

public class Startup(IConfiguration configuration, IEnumerable<string> args)
{
    private readonly IEnumerable<string> _args = args;
    private readonly OdinConfiguration _config = new(configuration);

    // This method gets called by the runtime. Use this method to add DI services.
    public void ConfigureServices(IServiceCollection services)
    {
        services.ConfigureSystemServices(_config);
    }

    // This method gets called by the runtime.
    // ConfigureContainer is where you can register things directly
    // with Autofac. This runs after ConfigureServices so the things
    // here will override registrations made in ConfigureServices.
    // This will all go in the ROOT CONTAINER and is NOT TENANT SPECIFIC.
    public void ConfigureContainer(ContainerBuilder builder)
    {
        builder.ConfigureSystemServices(_config);
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger, IHostApplicationLifetime lifetime)
    {
        logger.LogInformation("Environment: {Environment}", env.EnvironmentName);

        var config = app.ApplicationServices.GetRequiredService<OdinConfiguration>();

        // Note 1: see NotificationSocketController
        // Note 2: UseWebSockets must be before UseLoggingMiddleware
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        });

        app.UseLoggingMiddleware();
        app.UseMiddleware<OdinVersionNumberMiddleware>();

        if (env.IsProduction())
        {
            app.UseRateLimiter();
        }

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RedirectIfNotApexMiddleware>();
        app.UseMiddleware<CertesAcmeMiddleware>();

        app.UseHttpsPortRedirection(config.Host.DefaultHttpsPort);
        app.UseResponseCompression();
        app.UseHsts();

        // Provisioning mapping
        if (config.Registry.ProvisioningEnabled)
        {
            app.MapWhen(
                context => context.Request.Host.Host == config.Registry.ProvisioningDomain,
                a => Provisioning.Map(a, env, logger));
        }

        // Admin mapping
        if (config.Admin.ApiEnabled)
        {
            app.MapWhen(
                context => context.Request.Host.Host == config.Admin.Domain,
                a => Admin.Map(a, env, logger));
        }

        app.UseMultiTenancy();

        app.UseDefaultFiles();
        app.UseCertificateForwarding();
        app.UseStaticFiles();

        app.UseRouting();
        app.UseAuthentication();
#pragma warning disable ASP0001
        app.UseAuthorization();
#pragma warning restore ASP0001

        app.UseIdentityReadyState();
        app.UseVersionUpgrade();

        app.UseMiddleware<OdinContextMiddleware>();
        app.UseCors();
        app.UseApiCors();
        app.UseMiddleware<SharedSecretEncryptionMiddleware>();
        app.UseMiddleware<StaticFileCachingMiddleware>();

        app.UseEndpoints(endpoints =>
        {
            if (env.IsDevelopment())
            {
                endpoints.MapGet("/test-shutdown", async context =>
                {
                    var now = DateTime.UtcNow;
                    while (DateTime.UtcNow < now.AddSeconds(10))
                    {
                        logger.LogInformation("Waiting for shutdown");
                        await Task.Delay(1000);
                    }

                    await context.Response.WriteAsync("Done waiting for shutdown");
                });
            }

            endpoints.MapControllers();
        });

        // Intentionally for dev and production since we don't need to proxy anything
        app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments($"/{LinkPreviewDefaults.SsrPath}"),
            ssrApp =>
            {
                ssrApp.UseRouting();
                ssrApp.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });
        
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OdinCore v1"));

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
                homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3001/"); }); });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/feed"),
                homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3002/"); }); });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/chat"),
                homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3003/"); }); });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/mail"),
                homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3004/"); }); });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/community"),
                homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3006/"); }); });

            // No idea why this should be true instead of `ctx.Request.Path.StartsWithSegments("/")`
            app.MapWhen(ctx => true,
                homeApp =>
                {
                    homeApp.UseSpa(
                        spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3000/"); });
                });
        }
        else
        {
            logger.LogInformation("Mapping SPA paths on local disk");
            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
                ownerApp =>
                {
                    var ownerPath = Path.Combine(env.ContentRootPath, "client", "owner-app");
                    ownerApp.UseStaticFiles(new StaticFileOptions()
                    {
                        FileProvider = new PhysicalFileProvider(ownerPath),
                        RequestPath = "/owner"
                    });

                    ownerApp.Run(async context =>
                    {
                        context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                        await context.Response.SendFileAsync(Path.Combine(ownerPath, "index.html"));
                        return;
                    });
                });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/feed"),
                feedApp =>
                {
                    var feedPath = Path.Combine(env.ContentRootPath, "client", "apps", "feed");
                    feedApp.UseStaticFiles(new StaticFileOptions()
                    {
                        FileProvider = new PhysicalFileProvider(feedPath),
                        RequestPath = "/apps/feed"
                    });
                    feedApp.Run(async context =>
                    {
                        context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                        await context.Response.SendFileAsync(Path.Combine(feedPath, "index.html"));
                        return;
                    });
                });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/chat"),
                chatApp =>
                {
                    var chatPath = Path.Combine(env.ContentRootPath, "client", "apps", "chat");
                    chatApp.UseStaticFiles(new StaticFileOptions()
                    {
                        FileProvider = new PhysicalFileProvider(chatPath),
                        RequestPath = "/apps/chat"
                    });

                    chatApp.Run(async context =>
                    {
                        context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                        await context.Response.SendFileAsync(Path.Combine(chatPath, "index.html"));
                        return;
                    });
                });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/mail"),
                mailApp =>
                {
                    var mailPath = Path.Combine(env.ContentRootPath, "client", "apps", "mail");
                    mailApp.UseStaticFiles(new StaticFileOptions()
                    {
                        FileProvider = new PhysicalFileProvider(mailPath),
                        RequestPath = "/apps/mail"
                    });

                    mailApp.Run(async context =>
                    {
                        context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                        await context.Response.SendFileAsync(Path.Combine(mailPath, "index.html"));
                        return;
                    });
                });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/community"),
                communityApp =>
                {
                    var communityPath = Path.Combine(env.ContentRootPath, "client", "apps", "community");
                    communityApp.UseStaticFiles(new StaticFileOptions()
                    {
                        FileProvider = new PhysicalFileProvider(communityPath),
                        RequestPath = "/apps/community"
                    });

                    communityApp.Run(async context =>
                    {
                        context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                        await context.Response.SendFileAsync(Path.Combine(communityPath, "index.html"));
                        return;
                    });
                });
            
            app.MapWhen(ctx => true,
                homeApp =>
                {
                    var publicPath = Path.Combine(env.ContentRootPath, "client", "public-app");

                    homeApp.UseStaticFiles(new StaticFileOptions()
                    {
                        FileProvider = new PhysicalFileProvider(publicPath),
                        // RequestPath = "/"
                    });

                    homeApp.Run(async context =>
                    {
                        context.Response.Headers.ContentType = MediaTypeNames.Text.Html;

                        var svc = context.RequestServices.GetRequiredService<LinkPreviewService>();
                        var odinContext = context.RequestServices.GetRequiredService<IOdinContext>();

                        var indexFile = Path.Combine(publicPath, "index.html");

                        try
                        {
                            await svc.WriteIndexFileAsync(indexFile, odinContext);
                        }
                        catch (Exception)
                        {
                            // #super parnoid
                            await context.Response.SendFileAsync(indexFile);
                        }
                    });
                });
        }

        lifetime.ApplicationStarted.Register(() =>
        {
            // NOTE:
            // This is called AFTER the app has started and is accepting requests.
            // If you want stuff done BEFORE the app starts accepting requests,
            // put it in HostExtensions.BeforeApplicationStarting (below).
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            // NOTE:
            // This is called BEFORE the app has stopped accepting requests.
            logger.LogDebug("Waiting max {ShutdownTimeoutSeconds}s for requests and jobs to complete",
                config.Host.ShutdownTimeoutSeconds);

            var host = app.ApplicationServices.GetRequiredService<IHost>();
            host.BeforeApplicationStopping();
        });

        lifetime.ApplicationStopped.Register(() =>
        {
            // NOTE:
            // This is called AFTER the app has stopped accepting requests.
            // But it's not always being called. Or so it seems.
        });
    }

    //
}

//

public static class HostExtensions
{
    private static bool _didCleanUp;

    public static IHost BeforeApplicationStarting(this IHost host, string[] args)
    {
        _didCleanUp = false;

        var services = host.Services;
        var logger = services.GetRequiredService<ILogger<Startup>>();
        var config = services.GetRequiredService<OdinConfiguration>();

        logger.LogDebug("Starting initialization in {method}", nameof(BeforeApplicationStarting));

        // Create system database
        var systemDatabase = services.GetRequiredService<SystemDatabase>();
        systemDatabase.CreateDatabaseAsync().BlockingWait();

        // Load identity registry
        var registry = services.GetRequiredService<IIdentityRegistry>();
        registry.LoadRegistrations().BlockingWait();
        var certificateStore = services.GetRequiredService<ICertificateStore>();
        DevEnvironmentSetup.ConfigureIfPresent(logger, config, registry, certificateStore);

        // Check for singleton dependencies
        if (Env.IsDevelopment())
        {
            var root = services.GetRequiredService<IMultiTenantContainerAccessor>().Container();
            new AutofacDiagnostics(root, logger).AssertSingletonDependencies();
        }

        // Sanity ping cache
        logger.LogInformation("Level2CacheType: {Level2CacheType}", config.Cache.Level2CacheType);
        var cache = services.GetRequiredService<IGlobalLevel2Cache>();
        cache.Set("ping", "pong", TimeSpan.FromSeconds(1));
        var pong = cache.TryGet<string>("ping");
        if (pong != "pong")
        {
            throw new OdinSystemException("Cache sanity check failed");
        }

        // Ensure S3 bucket exists
        logger.LogInformation("S3PayloadStorage enabled: {enabled}", config.S3PayloadStorage.Enabled);
        if (config.S3PayloadStorage.Enabled)
        {
            logger.LogInformation("Creating S3 bucket '{BucketName}' at {ServiceUrl}",
                config.S3PayloadStorage.BucketName, config.S3PayloadStorage.ServiceUrl);
            var payloadBucket = services.GetRequiredService<IS3PayloadStorage>();
            payloadBucket.CreateBucketAsync().BlockingWait();
        }

        // Start system background services
        if (config.BackgroundServices.SystemBackgroundServicesEnabled)
        {
            services.StartSystemBackgroundServices().BlockingWait();
        }

        //
        // DON'T PUT ANY INITIALIZATION CODE BELOW THIS LINE
        //
        logger.LogDebug("Finished initialization in {method}", nameof(BeforeApplicationStarting));

        return host;
    }

    //

    public static IHost BeforeApplicationStopping(this IHost host)
    {
        if (_didCleanUp)
        {
            return host;
        }

        _didCleanUp = true;

        var services = host.Services;
        var logger = services.GetRequiredService<ILogger<Startup>>();

        logger.LogDebug("Starting clean up in {method}", nameof(BeforeApplicationStopping));

        //
        // Shutdown all tenant background services
        //
        services.ShutdownTenantBackgroundServices().BlockingWait();

        //
        // Shutdown system background services
        //
        services.ShutdownSystemBackgroundServices().BlockingWait();

        //
        // Wait for any registered fire-and-forget tasks to complete
        //
        services.GetRequiredService<IForgottenTasks>().WhenAll().BlockingWait();

        // DON'T PUT ANY CLEANUP BELOW THIS LINE
        logger.LogDebug("Finished clean up in {method}", nameof(BeforeApplicationStopping));

        return host;
    }

    //

}