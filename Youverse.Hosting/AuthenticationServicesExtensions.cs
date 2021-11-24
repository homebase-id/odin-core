using System;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Base;
using Youverse.Core.Util;
using Youverse.Hosting.Security;
using Youverse.Hosting.Security.Authentication;

namespace Youverse.Hosting
{
    public static class AuthenticationServicesExtensions
    {
        public static AuthenticationBuilder AddYouverseAuthentication(this IServiceCollection services /*, Action<AuthenticationOptions> configureOptions*/)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            // if (configureOptions == null)
            // {
            //     throw new ArgumentNullException(nameof(configureOptions));
            // }
            
            return services.AddAuthentication(options =>
                {
                    options.DefaultScheme = DotYouAuthConstants.ExternalDigitalIdentityClientCertificateScheme;
                    options.DefaultChallengeScheme = DotYouAuthConstants.ExternalDigitalIdentityClientCertificateScheme;
                })
                .AddScheme<DotIdentityOwnerAuthenticationSchemeOptions, DotIdentityOwnerAuthenticationHandler>(DotYouAuthConstants.DotIdentityOwnerScheme, op => { op.LoginUri = "/login"; })
                .AddCertificate(DotYouAuthConstants.ExternalDigitalIdentityClientCertificateScheme, options =>
                {
                    options.AllowedCertificateTypes = CertificateTypes.Chained;
                    options.ValidateCertificateUse = false; //HACK: to work around the fact that ISRG Root X1 is not set for Client Certificate authentication

                    //options.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                    //options.RevocationMode = X509RevocationMode.NoCheck

                    options.Events = new CertificateAuthenticationEvents()
                    {
                        OnCertificateValidated = CertificateValidated,
                        OnAuthenticationFailed = HandleAuthenticationFailed,
                        OnChallenge = HandleCertificateChallenge
                    };
                })
                //TODO: this certificate cache is not multi-tenant
                .AddCertificateCache(options =>
                {
                    //TODO: revisit this to see if it will serve as our re-validation method to ensure
                    // caller certs are still good 
                    options.CacheSize = 2048;
                    options.CacheEntryExpiration = TimeSpan.FromMinutes(10);
                });

        }

        private static Task CertificateValidated(CertificateValidatedContext context)
        {
            //todo: call out to additional service to validate more rules
            // i.e. blocked list
            // determine if this certificate is in my network an add extra claims
            //add system circle claim based on my relationship to this person
            //lookup name for the individual and add to the claims

            bool isTenantOwner = false;
            var dotYouContext = ScopedServicesDependencyInjectionExtensions.ResolveContext(context.HttpContext.RequestServices);
            using (var serverCertificate = dotYouContext.TenantCertificate.LoadCertificateWithPrivateKey())
            {
                //HACK: this is not sufficient for establishing the client and server certificates are the same.
                //https://eprint.iacr.org/2019/130.pdf - first few paragraphs
                isTenantOwner = serverCertificate.Thumbprint == context.ClientCertificate.Thumbprint;
            }

            //By logging in with a client certificate for this #prototrial, you are identified
            bool isIdentified = true;

            var bytes = context.ClientCertificate.Export(X509ContentType.Pkcs12);
            string clientCertificatePortable = Convert.ToBase64String(bytes);

            //TODO: PROTOTRIAL: assumes the certificate has a format where the domain is a common name. revisit
            string domain = CertificateUtils.GetDomainFromCommonName(context.ClientCertificate.Subject);

            //TODO: this needs to be moved to a central location so certificate auth can use it too
            string appId = context.HttpContext.Request.Headers[DotYouHeaderNames.AppId];

            Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(DotYouClaimTypes.IsIdentityOwner, isTenantOwner.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsIdentified, isIdentified.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.AppId, appId, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),

                new Claim(DotYouClaimTypes.IsAdminApp, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                
                //HACK: I don't know if this is a good idea to put this whole thing in the claims
                new Claim(DotYouClaimTypes.PublicKeyCertificate, clientCertificatePortable, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer)
            };

            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
            context.Success();
            return Task.CompletedTask;
        }

        private static Task HandleAuthenticationFailed(CertificateAuthenticationFailedContext context)
        {
            Console.WriteLine("Authentication Failed");
            return Task.CompletedTask;
        }

        private static Task HandleCertificateChallenge(CertificateChallengeContext context)
        {
            Console.WriteLine("handle cert challenge");
            return Task.CompletedTask;
        }
        
    }
}