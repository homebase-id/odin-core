using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using Autofac;
using Dawn;
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
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Workers.Transit;
using Youverse.Core.Services.Logging;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Core.Services.Workers.Certificate;
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
            services.Configure<KestrelServerOptions>(options => { options.AllowSynchronousIO = true; });

            var config = new YouverseConfiguration(Configuration);
            services.AddSingleton(config);

            PrepareEnvironment(config);
            AssertValidRenewalConfiguration(config.CertificateRenewal);

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

                    q.UseDefaultTransitOutboxSchedule(config.Quartz.BackgroundJobStartDelaySeconds, config.Quartz.ProcessOutboxIntervalSeconds);
                    q.UseDefaultCertificateRenewalSchedule(config.Quartz.BackgroundJobStartDelaySeconds,
                        config.Quartz.EnsureCertificateProcessorIntervalSeconds,
                        config.Quartz.ProcessPendingCertificateOrderIntervalInSeconds);
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
                c.IgnoreObsoleteActions();
                c.IgnoreObsoleteProperties();
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
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

            var ss = new ServerSystemStorage(config);
            
            var pendingTransferService = new PendingTransfersService(ss);
            services.AddSingleton(typeof(IPendingTransfersService), pendingTransferService);

            var certOrderListService = new PendingCertificateOrderListService(ss);
            services.AddSingleton(typeof(PendingCertificateOrderListService), certOrderListService);

            services.AddSingleton<IIdentityRegistrationService, IdentityRegistrationService>();

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
        public void Configure(IApplicationBuilder appx, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            // var config = new YouverseConfiguration(Configuration);

            bool IsProvisioningSite(HttpContext context)
            {
                var domain = context.RequestServices.GetService<YouverseConfiguration>()?.Registry.ProvisioningDomain;
                return context.Request.Host.Equals(new HostString(domain ?? ""));
            }
            
            appx.MapWhen(IsProvisioningSite, app => Provisioning.Map(app, env, logger));

            appx.MapWhen(ctx => !IsProvisioningSite(ctx), app =>
            {
                bool IsPathUsedForCertificateCreation(HttpContext context)
                {
                    var path = context.Request.Path;
                    bool isCertificateRegistrationPath = path.StartsWithSegments("/api/owner/v1/config/certificate") ||
                                                         path.StartsWithSegments("/.well-known/acme-challenge");
                    return isCertificateRegistrationPath && !context.Request.IsHttps;
                }

                app.MapWhen(IsPathUsedForCertificateCreation, certificateApp =>
                {
                    certificateApp.UseLoggingMiddleware();
                    certificateApp.UseMiddleware<ExceptionHandlingMiddleware>();
                    certificateApp.UseMultiTenancy();

                    certificateApp.UseDefaultFiles();
                    certificateApp.UseCertificateForwarding();
                    certificateApp.UseStaticFiles();

                    certificateApp.UseRouting();
                    certificateApp.UseAuthentication();
                    certificateApp.UseAuthorization();

                    certificateApp.UseEndpoints(endpoints => { endpoints.MapControllers(); });
                });

                app.MapWhen(ctx => !IsPathUsedForCertificateCreation(ctx), normalApp =>
                {
                    normalApp.UseLoggingMiddleware();
                    normalApp.UseMiddleware<ExceptionHandlingMiddleware>();
                    normalApp.UseMultiTenancy();

                    normalApp.UseDefaultFiles();
                    normalApp.UseCertificateForwarding();
                    normalApp.UseStaticFiles();

                    normalApp.UseRouting();
                    normalApp.UseAuthentication();
                    normalApp.UseAuthorization();

                    normalApp.UseMiddleware<DotYouContextMiddleware>();
                    normalApp.UseResponseCompression();
                    normalApp.UseMiddleware<SharedSecretEncryptionMiddleware>();
                    normalApp.UseMiddleware<StaticFileCachingMiddleware>();
                    normalApp.UseHttpsRedirection();

                    var webSocketOptions = new WebSocketOptions
                    {
                        KeepAliveInterval = TimeSpan.FromMinutes(2)
                    };

                    // webSocketOptions.AllowedOrigins.Add("https://...");
                    app.UseWebSockets(webSocketOptions);  //Note: see NotificationSocketController

                    normalApp.UseEndpoints(endpoints =>
                    {
                        endpoints.Map("/", async context => { context.Response.Redirect("/home"); });
                        endpoints.MapControllers();
                    });

                    if (env.IsDevelopment())
                    {
                        normalApp.UseSwagger();
                        normalApp.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DotYouCore v1"));

                        normalApp.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/home"),
                            homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dominion.id:3000/home/"); }); });

                        normalApp.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
                            homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dominion.id:3001/owner/"); }); });
                    }
                    else
                    {
                        logger.LogInformation("Mapping SPA paths on local disk");
                        normalApp.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
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


                        normalApp.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/home"),
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
                                    context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                                    await context.Response.SendFileAsync(Path.Combine(publicPath, "index.html"));
                                    return;
                                });
                            });
                    }
                });
            });
        }

        private void PrepareEnvironment(YouverseConfiguration cfg)
        {
            Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
            Directory.CreateDirectory(cfg.Host.SystemDataRootPath);
            Directory.CreateDirectory(cfg.Host.SystemSslRootPath);
        }

        private void AssertValidRenewalConfiguration(YouverseConfiguration.CertificateRenewalSection section)
        {
            Guard.Argument(section, nameof(section)).NotNull();
            Guard.Argument(section.CertificateAuthorityAssociatedEmail, nameof(section.CertificateAuthorityAssociatedEmail)).NotNull().NotEmpty();
            Guard.Argument(section.NumberOfCertificateValidationTries, nameof(section.NumberOfCertificateValidationTries)).Min(3);

            Guard.Argument(section.CsrCountryName, nameof(section.CsrCountryName)).NotNull().NotEmpty();
            Guard.Argument(section.CsrState, nameof(section.CsrState)).NotNull().NotEmpty();
            Guard.Argument(section.CsrLocality, nameof(section.CsrLocality)).NotNull().NotEmpty();
            Guard.Argument(section.CsrOrganization, nameof(section.CsrOrganization)).NotNull().NotEmpty();
            Guard.Argument(section.CsrOrganizationUnit, nameof(section.CsrOrganizationUnit)).NotNull().NotEmpty();
        }
    }
}