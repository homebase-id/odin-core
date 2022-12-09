using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Util;
using Youverse.Hosting.Authentication.CertificatePerimeter;

namespace Youverse.Hosting.Authentication.Perimeter
{
    public static class CertificatePerimeterAuthenticationExtensions
    {
        public static AuthenticationBuilder AddDiCertificateAuthentication(this AuthenticationBuilder builder, string schemeName)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            //return builder.AddCertificate(TransitPerimeterAuthConstants.TransitAuthScheme, options =>
            return builder.AddScheme<CertificateAuthenticationOptions, CertificateAuthenticationHandler>(schemeName, options =>
                {
                    options.AllowedCertificateTypes = CertificateTypes.Chained;
                    options.ValidateCertificateUse = false; //HACK: to work around the fact that ISRG Root X1 is not set for Client Certificate authentication

                    options.RevocationMode = X509RevocationMode.NoCheck; //HACK: need to revisit how revocation works.  it seems some certs are randomly revoked.

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
            // add system circle claim based on my relationship to this person
            // lookup name for the individual and add to the claims

            //TODO: PROTOTRIAL: assumes the certificate has a format where the domain is a common name. revisit
            string domain = CertificateUtils.GetDomainFromCommonName(context.ClientCertificate.Subject);
            
            // start hack
            //temp hack to continue until I build the letsencrypt cert system
            if (domain == "*.onekin.io")
            {
                 domain = context.Request.Headers["dns_hack"].FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new YouverseSecurityException("invalid certificate");
            }
            // end hack
            
            var claims = new List<Claim>()
            {
                new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
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