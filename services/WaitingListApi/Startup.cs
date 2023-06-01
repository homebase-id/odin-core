using System.Net.Mime;
using System.Reflection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;
using WaitingListApi.Config;
using WaitingListApi.Data;
using Youverse.Core.Serialization;

namespace WaitingListApi
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

            var config = new WaitingListConfig(Configuration);
            services.AddSingleton(config);

            PrepareEnvironment(config);

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    foreach (var c in DotYouSystemSerializer.JsonSerializerOptions!.Converters)
                    {
                        options.JsonSerializerOptions.Converters.Add(c);
                    }

                    options.JsonSerializerOptions.IncludeFields =
                        DotYouSystemSerializer.JsonSerializerOptions.IncludeFields;
                    options.JsonSerializerOptions.Encoder = DotYouSystemSerializer.JsonSerializerOptions.Encoder;
                    options.JsonSerializerOptions.MaxDepth = DotYouSystemSerializer.JsonSerializerOptions.MaxDepth;
                    options.JsonSerializerOptions.NumberHandling =
                        DotYouSystemSerializer.JsonSerializerOptions.NumberHandling;
                    options.JsonSerializerOptions.ReferenceHandler =
                        DotYouSystemSerializer.JsonSerializerOptions.ReferenceHandler;
                    options.JsonSerializerOptions.WriteIndented =
                        DotYouSystemSerializer.JsonSerializerOptions.WriteIndented;
                    options.JsonSerializerOptions.AllowTrailingCommas =
                        DotYouSystemSerializer.JsonSerializerOptions.AllowTrailingCommas;
                    options.JsonSerializerOptions.DefaultBufferSize =
                        DotYouSystemSerializer.JsonSerializerOptions.DefaultBufferSize;
                    options.JsonSerializerOptions.DefaultIgnoreCondition =
                        DotYouSystemSerializer.JsonSerializerOptions.DefaultIgnoreCondition;
                    options.JsonSerializerOptions.DictionaryKeyPolicy =
                        DotYouSystemSerializer.JsonSerializerOptions.DictionaryKeyPolicy;
                    options.JsonSerializerOptions.PropertyNamingPolicy =
                        DotYouSystemSerializer.JsonSerializerOptions.PropertyNamingPolicy;
                    options.JsonSerializerOptions.ReadCommentHandling =
                        DotYouSystemSerializer.JsonSerializerOptions.ReadCommentHandling;
                    options.JsonSerializerOptions.UnknownTypeHandling =
                        DotYouSystemSerializer.JsonSerializerOptions.UnknownTypeHandling;
                    options.JsonSerializerOptions.IgnoreReadOnlyFields =
                        DotYouSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyFields;
                    options.JsonSerializerOptions.IgnoreReadOnlyProperties =
                        DotYouSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyProperties;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive =
                        DotYouSystemSerializer.JsonSerializerOptions.PropertyNameCaseInsensitive;
                });

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
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
                c.SwaggerDoc("v1", new()
                {
                    Title = "Waiting list API",
                    Version = "v1"
                });
            });

            // services.AddAuthentication();
            // services.AddAuthorization(policy =>
            // {
            //     SystemPolicies.AddPolicies(policy);
            // });

            services.AddSingleton<WaitingListConfig>(config);
            services.AddSingleton<WaitingListStorage>();

            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "client/"; });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger, IHostApplicationLifetime lifetime)
        {
            var config = app.ApplicationServices.GetRequiredService<WaitingListConfig>();

            // app.UseLoggingMiddleware();
            // app.UseMiddleware<ExceptionHandlingMiddleware>();


            app.UseDefaultFiles();
            app.UseCertificateForwarding();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseResponseCompression();
            // app.UseCors(c => c.WithOrigins(new[] { "" }));
            app.UseHttpsRedirection();

            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            };

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    context.Response.Redirect("/home");
                    await Task.CompletedTask;
                });
                endpoints.MapControllers();
            });

            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DotYouCore v1"));

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/home"),
                    homeApp =>
                    {
                        homeApp.UseSpa(
                            spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3007/"); });
                    });
            }
            else
            {
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
                            context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                            await context.Response.SendFileAsync(Path.Combine(publicPath, "index.html"));
                            return;
                        });
                    });
            }

            // lifetime.ApplicationStarted.Register(() => { DevEnvironmentSetup.ConfigureIfPresent(config, registry); });
        }

        private void PrepareEnvironment(WaitingListConfig cfg)
        {
            Directory.CreateDirectory(cfg.Host.SystemDataRootPath);
        }
    }
}