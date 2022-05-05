using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Util;


namespace Youverse.Hosting.Authentication.TransitPerimeter
{
    public static class TransitPerimeterAuthenticationExtensions
    {
        public static AuthenticationBuilder AddTransitPerimeterAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            //return builder.AddCertificate(TransitPerimeterAuthConstants.TransitAuthScheme, options =>
            return builder.AddScheme<CertificateAuthenticationOptions, TransitCertificateAuthenticationHandler>(TransitPerimeterAuthConstants.TransitAuthScheme, options =>
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
                    // TODO: revisit this to see if it will serve as our re-validation method to ensure
                    //  caller certs are still good 
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

            //TODO: PROTOTRIAL: assumes the certificate has a format where the domain is a common name. revisit
            string domain = CertificateUtils.GetDomainFromCommonName(context.ClientCertificate.Subject);

            var claims = new List<Claim>()
            {
                new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.DeviceUid64, string.Empty, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer)
            };
            
            //TODO: need to app permission negations at the app level, meaning that while sam can form operations; he cannot perform those operations when using a specific app
            string appIdValue = context.HttpContext.Request.Headers[DotYouHeaderNames.AppId];
            
            if (Guid.TryParse(appIdValue, out var appId) && appId != Guid.Empty)
            {
                var appRegSvc = context.HttpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
                var appReg = appRegSvc.GetAppRegistration(appId).GetAwaiter().GetResult();

                var isValidApp = appReg is {IsRevoked: false};
                if (!isValidApp)
                {
                    throw new YouverseSecurityException($"Invalid AppId {appId}");
                }

                var c1 = new Claim(DotYouClaimTypes.IsAuthorizedApp, isValidApp.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer);
                var c2 = new Claim(DotYouClaimTypes.AppId, appId.ToString(), ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer);
                claims.Add(c1);
                claims.Add(c2);
            }

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