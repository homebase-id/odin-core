using DotYou.Kernel;
using DotYou.Kernel.Services.Verification;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace DotYou.TenantHost
{
    public class Startup
    {
        private Task ValidateCertificate(CertificateValidatedContext context)
        {
            //todo: call out to additonal service to validate more rules
            // i.e. blocked list
            // determine if this certificate is in my network an add extra claims
            //add systemcircle claim based on my relationshiop to this person
            //lookup name for the individual and add to the claims

            /*
            var claims = new[]
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        context.ClientCertificate.Subject,
                        ClaimValueTypes.String,
                        context.Options.ClaimsIssuer),

                };

            context.Principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, context.Scheme.Name));

            */

            context.Success();
            return Task.CompletedTask;
        }

        private Task HandleAuthenticationFailed(CertificateAuthenticationFailedContext context)
        {

            return Task.CompletedTask;
        }

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
                       OnCertificateValidated = context => this.ValidateCertificate(context),
                       OnAuthenticationFailed = context => this.HandleAuthenticationFailed(context)
                   };

               })
               .AddCertificateCache(options =>
               {
                    //TODO: revist this to see if it will serve as our re-validation method to ensure
                    // caller certs are still good 
                    options.CacheSize = 2048;
                   options.CacheEntryExpiration = TimeSpan.FromMinutes(10);
               });
            
            services.AddSingleton<IDotYouHttpClientProxy, DotYouHttpClientProxy>();
            //services.AddSingleton<IMemoryCache, MultiTenantMemoryCache>();
            services.AddMemoryCache();
            services.AddSingleton<ISenderVerificationService, SenderVerificationService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseAuthentication();
            app.UseRouting();

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
    }
}
