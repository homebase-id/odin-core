using Microsoft.AspNetCore.Server.Kestrel.Core;
using Odin.Core.Serialization;
using Serilog;
using WaitingListApi.Config;
using WaitingListApi.Data;

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

            Log.Information($"CorsUrl: [{config.Host.CorsUrl}]");
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
                    foreach (var c in OdinSystemSerializer.JsonSerializerOptions!.Converters)
                    {
                        options.JsonSerializerOptions.Converters.Add(c);
                    }

                    options.JsonSerializerOptions.IncludeFields =
                        OdinSystemSerializer.JsonSerializerOptions.IncludeFields;
                    options.JsonSerializerOptions.Encoder = OdinSystemSerializer.JsonSerializerOptions.Encoder;
                    options.JsonSerializerOptions.MaxDepth = OdinSystemSerializer.JsonSerializerOptions.MaxDepth;
                    options.JsonSerializerOptions.NumberHandling =
                        OdinSystemSerializer.JsonSerializerOptions.NumberHandling;
                    options.JsonSerializerOptions.ReferenceHandler =
                        OdinSystemSerializer.JsonSerializerOptions.ReferenceHandler;
                    options.JsonSerializerOptions.WriteIndented =
                        OdinSystemSerializer.JsonSerializerOptions.WriteIndented;
                    options.JsonSerializerOptions.AllowTrailingCommas =
                        OdinSystemSerializer.JsonSerializerOptions.AllowTrailingCommas;
                    options.JsonSerializerOptions.DefaultBufferSize =
                        OdinSystemSerializer.JsonSerializerOptions.DefaultBufferSize;
                    options.JsonSerializerOptions.DefaultIgnoreCondition =
                        OdinSystemSerializer.JsonSerializerOptions.DefaultIgnoreCondition;
                    options.JsonSerializerOptions.DictionaryKeyPolicy =
                        OdinSystemSerializer.JsonSerializerOptions.DictionaryKeyPolicy;
                    options.JsonSerializerOptions.PropertyNamingPolicy =
                        OdinSystemSerializer.JsonSerializerOptions.PropertyNamingPolicy;
                    options.JsonSerializerOptions.ReadCommentHandling =
                        OdinSystemSerializer.JsonSerializerOptions.ReadCommentHandling;
                    options.JsonSerializerOptions.UnknownTypeHandling =
                        OdinSystemSerializer.JsonSerializerOptions.UnknownTypeHandling;
                    options.JsonSerializerOptions.IgnoreReadOnlyFields =
                        OdinSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyFields;
                    options.JsonSerializerOptions.IgnoreReadOnlyProperties =
                        OdinSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyProperties;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive =
                        OdinSystemSerializer.JsonSerializerOptions.PropertyNameCaseInsensitive;
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

            app.UseCors(WaitingListCorsPolicy);

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });

            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "homebase.id v1"));
            // lifetime.ApplicationStarted.Register(() => { DevEnvironmentSetup.ConfigureIfPresent(config, registry); });
        }

        private void PrepareEnvironment(WaitingListConfig cfg)
        {
            Directory.CreateDirectory(cfg.Host.SystemDataRootPath);
        }
    }
}
