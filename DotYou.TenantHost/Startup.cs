using DotYou.Kernel;
using DotYou.Kernel.Identity;
using DotYou.Kernel.Services.Identity;
using DotYou.Kernel.Services.TrustNetwork;
using DotYou.Types;
using DotYou.Types.TrustNetwork;
using LiteDB;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DotYou.TenantHost
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
            services.AddHttpContextAccessor();

            services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
               .AddCertificate(options =>
               {
                   options.AllowedCertificateTypes = CertificateTypes.Chained;
                   options.ValidateCertificateUse = true;

                   //options.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                   //options.RevocationMode = X509RevocationMode.NoCheck

                   options.Events = new CertificateAuthenticationEvents()
                   {
                       OnCertificateValidated = context => this.CertificateValidated(context),
                       OnAuthenticationFailed = context => this.HandleAuthenticationFailed(context),
                       OnChallenge = context => this.HandleCertificateChallenge(context)
                   };
               })
               //TODO: this certificate cache is not multi-tenant
               .AddCertificateCache(options =>
               {
                   //TODO: revist this to see if it will serve as our re-validation method to ensure
                   // caller certs are still good 
                   options.CacheSize = 2048;
                   options.CacheEntryExpiration = TimeSpan.FromMinutes(10);
               });

            //TODO
            //dotnet add package PersistentMemoryCache --version 1.0.15-Beta
            services.AddMemoryCache();
            
            services.AddScoped<ITrustNetworkService, TrustNetworkService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<TrustNetworkService>>();
                
                return new TrustNetworkService(context, logger);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            this.ConfigureLiteDBSerialization();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //order matters
            app.UseRouting();
            app.UseAuthentication();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                if (env.IsDevelopment())
                {
                    endpoints.MapGet("/", async context =>
                    {
                        await context.Response.WriteAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                    });
                }
            });
        }

        /// <summary>
        /// Gets the DotYouContext for the given Service Scope.
        /// </summary>
        private DotYouContext ResolveContext(IServiceProvider svc)
        {
            var accessor = svc.GetRequiredService<IHttpContextAccessor>();
            var reg = svc.GetRequiredService<IIdentityContextRegistry>();

            var context = reg.ResolveContext(accessor.HttpContext.Request.Host.Host);
            return context;
        }

        private Task CertificateValidated(CertificateValidatedContext context)
        {
            //todo: call out to additonal service to validate more rules
            // i.e. blocked list
            // determine if this certificate is in my network an add extra claims
            //add systemcircle claim based on my relationshiop to this person
            //lookup name for the individual and add to the claims

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, context.ClientCertificate.Subject, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, context.ClientCertificate.Subject, ClaimValueTypes.String, context.Options.ClaimsIssuer),

            };

            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
            context.Success();
            return Task.CompletedTask;
        }

        private Task HandleAuthenticationFailed(CertificateAuthenticationFailedContext context)
        {
            Console.WriteLine("Authentication Failed");
            return Task.CompletedTask;
        }

        private Task HandleCertificateChallenge(CertificateChallengeContext context)
        {
            Console.WriteLine("handle cert challenge");
            return Task.CompletedTask;
        }

        private void ConfigureLiteDBSerialization()
        {
            //see: Register our custom type @ https://www.litedb.org/docs/object-mapping/   
            BsonMapper.Global.RegisterType<DotYouIdentity>(
                serialize: (identity) => identity.ToString(),
                deserialize: (bson) => new DotYouIdentity(bson.AsString)
                );
        }
    }
}
