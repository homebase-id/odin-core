using System;
using System.Buffers;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autofac;
using LiteDB;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Quartz;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Workers.Transit;
using Youverse.Core.Services.Logging;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.Perimeter;
using Youverse.Hosting.Authentication.System;
using Youverse.Hosting.Middleware;
using Youverse.Hosting.Middleware.Logging;
using Youverse.Hosting.Multitenant;

namespace Youverse.Hosting
{
    public class Startup
    {
        private IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            //HACK: why is this suddenly needed!? 
            services.Configure<KestrelServerOptions>(options => { options.AllowSynchronousIO = true; });

            var config = new Configuration(Configuration);
            services.AddSingleton(config);

            PrepareEnvironment(config);

            if (config.Quartz.EnableQuartzBackgroundService)
            {
                services.AddQuartz(q =>
                {
                    //lets use use our normal DI setup
                    q.UseMicrosoftDependencyInjectionJobFactory();
                    // q.UseDedicatedThreadPool(options =>
                    // {
                    //     options.MaxConcurrency = 10; //TODO: good idea?
                    // });

                    q.UseDefaultTransitOutboxSchedule(config.Quartz.BackgroundJobStartDelaySeconds);
                });

                services.AddQuartzServer(options => { options.WaitForJobsToComplete = true; });
            }

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    foreach (var c in DotYouSystemSerializer.JsonSerializerOptions!.Converters)
                    {
                        options.JsonSerializerOptions.Converters.Add(c);
                    }

                    options.JsonSerializerOptions.IncludeFields = DotYouSystemSerializer.JsonSerializerOptions.IncludeFields;
                    options.JsonSerializerOptions.Encoder = DotYouSystemSerializer.JsonSerializerOptions.Encoder;
                    options.JsonSerializerOptions.MaxDepth = DotYouSystemSerializer.JsonSerializerOptions.MaxDepth;
                    options.JsonSerializerOptions.NumberHandling = DotYouSystemSerializer.JsonSerializerOptions.NumberHandling;
                    options.JsonSerializerOptions.ReferenceHandler = DotYouSystemSerializer.JsonSerializerOptions.ReferenceHandler;
                    options.JsonSerializerOptions.WriteIndented = DotYouSystemSerializer.JsonSerializerOptions.WriteIndented;
                    options.JsonSerializerOptions.AllowTrailingCommas = DotYouSystemSerializer.JsonSerializerOptions.AllowTrailingCommas;
                    options.JsonSerializerOptions.DefaultBufferSize = DotYouSystemSerializer.JsonSerializerOptions.DefaultBufferSize;
                    options.JsonSerializerOptions.DefaultIgnoreCondition = DotYouSystemSerializer.JsonSerializerOptions.DefaultIgnoreCondition;
                    options.JsonSerializerOptions.DictionaryKeyPolicy = DotYouSystemSerializer.JsonSerializerOptions.DictionaryKeyPolicy;
                    options.JsonSerializerOptions.PropertyNamingPolicy = DotYouSystemSerializer.JsonSerializerOptions.PropertyNamingPolicy;
                    options.JsonSerializerOptions.ReadCommentHandling = DotYouSystemSerializer.JsonSerializerOptions.ReadCommentHandling;
                    options.JsonSerializerOptions.UnknownTypeHandling = DotYouSystemSerializer.JsonSerializerOptions.UnknownTypeHandling;
                    options.JsonSerializerOptions.IgnoreReadOnlyFields = DotYouSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyFields;
                    options.JsonSerializerOptions.IgnoreReadOnlyProperties = DotYouSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyProperties;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = DotYouSystemSerializer.JsonSerializerOptions.PropertyNameCaseInsensitive;
                });

            //services.AddRazorPages(options => { options.RootDirectory = "/Views"; });

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
            services.AddHttpContextAccessor();
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.MimeTypes = new[] { "application/json" };
            });
            
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
                c.EnableAnnotations();
                c.SwaggerDoc("v1", new()
                {
                    Title = "DotYouCore API",
                    Version = "v1"
                });
            });

            services.AddAuthentication(options => { })
                .AddOwnerAuthentication()
                .AddClientTokenAuthentication()
                .AddDiCertificateAuthentication(PerimeterAuthConstants.TransitCertificateAuthScheme)
                .AddDiCertificateAuthentication(PerimeterAuthConstants.PublicTransitAuthScheme)
                .AddSystemAuthentication();

            services.AddAuthorization(policy =>
            {
                OwnerPolicies.AddPolicies(policy);
                SystemPolicies.AddPolicies(policy);
                ClientTokenPolicies.AddPolicies(policy);
                CertificatePerimeterPolicies.AddPolicies(policy, PerimeterAuthConstants.TransitCertificateAuthScheme);
                CertificatePerimeterPolicies.AddPolicies(policy, PerimeterAuthConstants.PublicTransitAuthScheme);
            });

            //services.AddSingleton<IPendingTransfersService, PendingTransfersService>();
            var pendingTransferService = new PendingTransfersService(config.Host.SystemDataRootPath);
            services.AddSingleton(typeof(IPendingTransfersService), pendingTransferService);
            
            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "client/"; });
        }

        // ConfigureContainer is where you can register things directly
        // with Autofac. This runs after ConfigureServices so the things
        // here will override registrations made in ConfigureServices.
        // Don't build the container; that gets done for you. If you
        // need a reference to the container, you need to use the
        // "Without ConfigureContainer" mechanism shown later.
        public void ConfigureContainer(ContainerBuilder builder)
        {
            /*
            AUTOFAC CHEAT SHEET (https://stackoverflow.com/questions/42809618/migration-from-asp-net-cores-container-to-autofac)
            ASP.NET Core container             -> Autofac
            ----------------------                -------
            // the 3 big ones
            services.AddSingleton<IFoo, Foo>() -> builder.RegisterType<Foo>().As<IFoo>().SingleInstance()
            services.AddScoped<IFoo, Foo>()    -> builder.RegisterType<Foo>().As<IFoo>().InstancePerLifetimeScope()
            services.AddTransient<IFoo, Foo>() -> builder.RegisterType<Foo>().As<IFoo>().InstancePerDependency()
            // default
            services.AddTransient<IFoo, Foo>() -> builder.RegisterType<Foo>().As<IFoo>()
            // multiple
            services.AddX<IFoo1, Foo>();
            services.AddX<IFoo2, Foo>();       -> builder.RegisterType<Foo>().As<IFoo1>().As<IFoo2>().X()
            // without interface
            services.AddX<Foo>()               -> builder.RegisterType<Foo>().AsSelf().X()
            */

            // This will all go in the ROOT CONTAINER and is NOT TENANT SPECIFIC.
            //builder.RegisterType<Controllers.Test.TenantDependencyTest2>().As<Controllers.Test.ITenantDependencyTest2>().SingleInstance();
            builder.RegisterModule(new LoggingAutofacModule());
            builder.RegisterModule(new MultiTenantAutofacModule());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            app.UseLoggingMiddleware();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseMultiTenancy();

            app.UseDefaultFiles();
            app.UseCertificateForwarding();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<DotYouContextMiddleware>();
            app.UseResponseCompression();
            app.UseMiddleware<SharedSecretEncryptionMiddleware>();
 

            // app.UseWebSockets();
            // app.Map("/owner/api/live/notifications", appBuilder => appBuilder.UseMiddleware<NotificationWebSocketMiddleware>());

            app.UseEndpoints(endpoints =>
            {
                endpoints.Map("/", async context => { context.Response.Redirect("/home"); });
                endpoints.MapControllers();
            });

            //Note: I have ZERO clue why you have to use a .MapWhen versus .map
            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DotYouCore v1"));

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/home"),
                    homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dominion.id:3000/home/"); }); });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
                    homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dominion.id:3001/owner/"); }); });
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
                            await context.Response.SendFileAsync(Path.Combine(ownerPath, "index.html"));
                            return;
                        });
                    });


                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/home"),
                    homeApp =>
                    {
                        var publicPath = Path.Combine(env.ContentRootPath, "client", "public-app");

                        homeApp.UseStaticFiles(new StaticFileOptions()
                        {
                            FileProvider = new PhysicalFileProvider(publicPath),
                            RequestPath = "/home"
                        });

                        homeApp.Run(async context =>
                        {
                            await context.Response.SendFileAsync(Path.Combine(publicPath, "index.html"));
                            return;
                        });
                    });

                //
            }

            //redirect everything else to root so the default behavior can start (i.e. clientside rendering)
            //TODO: not sure I like this since it means we'll miss 404s.  will need to consider
            // app.Run(async (context) =>
            // {
            //     context.Response.Redirect("/");
            // });
        }

        private void PrepareEnvironment(Configuration cfg)
        {
            Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
            Directory.CreateDirectory(cfg.Host.TempTenantDataRootPath);
        }
    }
}