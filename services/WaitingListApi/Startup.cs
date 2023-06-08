using Microsoft.AspNetCore.Server.Kestrel.Core;
using WaitingListApi.Config;
using WaitingListApi.Data;
using Youverse.Core.Serialization;

namespace WaitingListApi
{
    public class Startup
    {
        private const string WaitingListCorsPolicy = "waiting_list";
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

            services.AddCors(setup =>
            {
                setup.AddPolicy(WaitingListCorsPolicy, p =>
                {
                    p.WithOrigins(config.Host.CorsUrl)
                        .WithHeaders("POST").WithHeaders("Content-Type");
                });
            });

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

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.IgnoreObsoleteActions();
                c.IgnoreObsoleteProperties();
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
                // c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
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
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger, IHostApplicationLifetime lifetime)
        {
            // var config = app.ApplicationServices.GetRequiredService<WaitingListConfig>();

            // app.UseLoggingMiddleware();
            // app.UseMiddleware<ExceptionHandlingMiddleware>();

            app.UseCertificateForwarding();
            app.UseCors(WaitingListCorsPolicy);

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseResponseCompression();
            // app.UseHttpsRedirection();

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });

            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DotYouCore v1"));
            // lifetime.ApplicationStarted.Register(() => { DevEnvironmentSetup.ConfigureIfPresent(config, registry); });
        }

        private void PrepareEnvironment(WaitingListConfig cfg)
        {
            Directory.CreateDirectory(cfg.Host.SystemDataRootPath);
        }
    }
}
