using System;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dawn;
using LiteDB;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Workers.Transit;
using Youverse.Hosting.Controllers.Perimeter;
using Youverse.Services.Messaging.Chat;

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
            var config = this.Configuration.GetSection("Config").Get<Config>();
            AssertValidConfiguration(config);
            PrepareEnvironment(config);

            if (config.EnableQuartzBackgroundService)
            {
                services.AddQuartz(q =>
                {
                    //lets use use our normal DI setup
                    q.UseMicrosoftDependencyInjectionJobFactory();
                    // q.UseDedicatedThreadPool(options =>
                    // {
                    //     options.MaxConcurrency = 10; //TODO: good idea?
                    // });

                    q.UseDefaultTransitOutboxSchedule(config.BackgroundJobStartDelaySeconds);
                });

                services.AddQuartzServer(options => { options.WaitForJobsToComplete = true; });
            }
            
            services.AddControllers(config =>
                {
                    config.Filters.Add(new ApplyPerimeterMetaData());
                    //config.OutputFormatters.RemoveType<HttpNoContentOutputFormatter>(); //removes content type when 204 is returned.
                }
            ).AddJsonOptions(options => { options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

            //services.AddRazorPages(options => { options.RootDirectory = "/Views"; });

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
            services.AddHttpContextAccessor();
            
            services.AddYouverseAuthentication();
            services.AddYouverseAuthorization();

            services.AddMemoryCache();
            services.AddSignalR(options => { options.EnableDetailedErrors = true; });

            //TODO: this service is one of the only singletons because it's used by the 
            services.AddYouVerseScopedServices();

            services.AddSingleton<IPendingTransfersService, PendingTransfersService>();
            
            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "ClientApp/build"; });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            this.ConfigureLiteDBSerialization();

            if (env.IsDevelopment())
            {
                //app.UseWebAssemblyDebugging();
                app.UseDeveloperExceptionPage();
            }

            app.UseCertificateForwarding();
            app.UseMiddleware<ExceptionMiddleware>();
            app.UseStaticFiles();
            app.UseSpaStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                // endpoints.Map("/", async context =>
                // {
                //     await context.Response.WriteAsync("wrinkle");
                // });

                endpoints.MapControllers();
                //endpoints.MapControllerRoute("api", "api/{controller}/{action=Index}/{id?}");
                //endpoints.MapFallbackToFile("index.html");

                endpoints.MapHub<NotificationHub>("/api/live/notifications", o =>
                {
                    //TODO: for #prototrial, i narrowed this to websockets
                    //only so i could disable negotiation from the client
                    //as it was causing issues with authentication.
                    o.Transports = HttpTransportType.WebSockets;
                });

                endpoints.MapHub<MessagingHub>("/api/live/chat", o =>
                {
                    //TODO: for #prototrial, i narrowed this to websockets
                    //only so i could disable negotiation from the client
                    //as it was causing issues with authentication.
                    o.Transports = HttpTransportType.WebSockets;
                });
            });

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "ClientApp";
                if (env.IsDevelopment())
                {
                    spa.UseReactDevelopmentServer(npmScript: "start");
                }
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
                    memberMapper.Serialize = (obj, mapper) => serialize((DotYouIdentity)obj);
                    memberMapper.Deserialize = (value, mapper) => deserialize(value);
                }
            };

            // BsonMapper.Global.Entity<DotYouProfile>()
            //     .Id(x => x.DotYouId);
            // BsonMapper.Global.Entity<NoncePackage>()
            //     .Id(x => new Guid(Convert.FromBase64String(x.Nonce64)));
        }

        private void AssertValidConfiguration(Config cfg)
        {
            Guard.Argument(cfg, nameof(cfg)).NotNull();

            //HACK: until I merge with Seb Autofac 
            var useLocalReg = Environment.GetEnvironmentVariable("USE_LOCAL_DOTYOU_CERT_REGISTRY", EnvironmentVariableTarget.Process) == "1";
            if (useLocalReg == false)
            {
                Guard.Argument(cfg.RegistryServerUri, nameof(cfg.RegistryServerUri)).NotNull().NotEmpty();
                Guard.Argument(Uri.IsWellFormedUriString(cfg.RegistryServerUri, UriKind.Absolute), nameof(cfg.RegistryServerUri)).True();
            }

            Guard.Argument(cfg.TenantDataRootPath, nameof(cfg.TenantDataRootPath)).NotNull().NotEmpty();
            Guard.Argument(cfg.TempTenantDataRootPath, nameof(cfg.TempTenantDataRootPath)).NotNull().NotEmpty();
        }

        private void PrepareEnvironment(Config cfg)
        {
            Directory.CreateDirectory(cfg.TenantDataRootPath);
            Directory.CreateDirectory(cfg.TempTenantDataRootPath);
        }
    }
}