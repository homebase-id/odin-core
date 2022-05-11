using System;
using System.IO;
using System.Text.Json.Serialization;
using Autofac;
using LiteDB;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Quartz;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Workers.Transit;
using Youverse.Core.Services.Logging;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.CertificatePerimeter;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.Perimeter;
using Youverse.Hosting.Authentication.YouAuth;
using Youverse.Hosting.Controllers.TransitPerimeter;
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
            services.Configure<KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });
            
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

            services.AddControllers(options =>
                {
                    // options.Filters.Add(new ApplyPerimeterMetaData());
                    //config.OutputFormatters.RemoveType<HttpNoContentOutputFormatter>(); //removes content type when 204 is returned.
                }
            ).AddJsonOptions(options => { options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

            //services.AddRazorPages(options => { options.RootDirectory = "/Views"; });

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
            services.AddHttpContextAccessor();

            services.AddAuthentication(options => { })
                .AddOwnerAuthentication()
                .AddYouAuthAuthentication()
                .AddAppAuthentication()
                .AddDiCertificateAuthentication(PerimeterAuthConstants.TransitCertificateAuthScheme)
                .AddDiCertificateAuthentication(PerimeterAuthConstants.NotificationCertificateAuthScheme);

            services.AddAuthorization(policy =>
            {
                OwnerPolicies.AddPolicies(policy);
                AppPolicies.AddPolicies(policy);
                CertificatePerimeterPolicies.AddPolicies(policy, PerimeterAuthConstants.TransitCertificateAuthScheme);
                CertificatePerimeterPolicies.AddPolicies(policy, PerimeterAuthConstants.NotificationCertificateAuthScheme);
                YouAuthPolicies.AddPolicies(policy);
            });

            services.AddSingleton<IPendingTransfersService, PendingTransfersService>();

            // In production, the React files will be served from this directory
            //services.AddSpaStaticFiles(configuration => { configuration.RootPath = "ClientApp/build"; });
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

            this.ConfigureLiteDBSerialization();

            if (env.IsDevelopment())
            {
                //app.UseWebAssemblyDebugging();
                //app.UseDeveloperExceptionPage();
            }

            app.UseCertificateForwarding();
            app.UseStaticFiles();
            //app.UseSpaStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<DotYouContextMiddleware>();

            app.UseWebSockets();
            app.Map("/owner/api/live/notifications", appBuilder => appBuilder.UseMiddleware<NotificationWebSocketMiddleware>());

            app.UseEndpoints(endpoints =>
            {
                endpoints.Map("/", async context => { context.Response.Redirect("/home"); });
                endpoints.MapControllers();
            });
            
            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner")
                , adminApp =>
                {
                    adminApp.UseSpa(spa =>
                    {
                        if (env.IsDevelopment())
                        {
                            // spa.UseProxyToSpaDevelopmentServer("http://localhost:3001/owner");
                            spa.UseProxyToSpaDevelopmentServer($"https://dominion.id:3001/owner/");
                            //spa.UseProxyToSpaDevelopmentServer($"https://dominion.id.me:3001/owner");
                        }
                        else
                        {
                            //TODO: setup to read from config in production (CDN Or otherwise)
                            spa.Options.SourcePath = @"Client/owner-console";
                            spa.Options.DefaultPage = "/index.html";
                            spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions
                            {
                                RequestPath = "/owner",
                                FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "Client", "owner-console"))
                            };
                        }
                    });
                });

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/home"), landingPageApp =>
            {
                landingPageApp.UseSpa(spa =>
                {
                    if (env.IsDevelopment())
                    {
                        // spa.UseProxyToSpaDevelopmentServer("http://localhost:3000/home");
                        spa.UseProxyToSpaDevelopmentServer($"https://dominion.id:3000/home/");
                    }
                    else
                    {
                        //TODO: setup to read from config in production (CDN Or otherwise)
                        spa.Options.SourcePath = @"Client/public-app";
                        spa.Options.DefaultPage = "/index.html";
                        spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions
                        {
                            RequestPath = "/home",
                            FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "Client", "public-app"))
                        };
                    }
                });
            });
        }

        private void ConfigureLiteDBSerialization()
        {
            var serialize = new Func<DotYouIdentity, BsonValue>(identity => identity.ToString());
            var deserialize = new Func<BsonValue, DotYouIdentity>(bson => new DotYouIdentity(bson.AsString));

            //see: Register our custom type @ https://www.litedb.org/docs/object-mapping/   
            BsonMapper.Global.RegisterType<DotYouIdentity>(
                serialize: serialize,
                deserialize: deserialize
            );

            BsonMapper.Global.ResolveMember = (type, memberInfo, memberMapper) =>
            {
                if (memberMapper.DataType == typeof(DotYouIdentity))
                {
                    //memberMapper.Serialize = (obj, mapper) => new BsonValue(((DotYouIdentity) obj).ToString());
                    memberMapper.Serialize = (obj, mapper) => serialize((DotYouIdentity) obj);
                    memberMapper.Deserialize = (value, mapper) => deserialize(value);
                }
            };

            // BsonMapper.Global.Entity<DotYouProfile>()
            //     .Id(x => x.DotYouId);
            // BsonMapper.Global.Entity<NoncePackage>()
            //     .Id(x => new Guid(Convert.FromBase64String(x.Nonce64)));
        }

        private void PrepareEnvironment(Configuration cfg)
        {
            Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
            Directory.CreateDirectory(cfg.Host.TempTenantDataRootPath);
        }
    }
}