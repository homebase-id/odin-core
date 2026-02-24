using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;
using Odin.Services.Authorization;
using Odin.Core.Util;

namespace Odin.Hosting.Authentication.Peer
{
    public static class PeerCertificateAuthenticationExtensions
    {
        public static AuthenticationBuilder AddPeerCertificateAuthentication(this AuthenticationBuilder builder, string schemeName)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            //return builder.AddCertificate(TransitPerimeterAuthConstants.TransitAuthScheme, options =>
            return builder.AddScheme<CertificateAuthenticationOptions, PeerCertificateAuthenticationHandler>(schemeName, options =>
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
            
            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new OdinSecurityException("invalid certificate");
            }


            var claims = new List<Claim>()
            {
                new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(OdinClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
                new Claim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
            };

            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
            context.Success();
            return Task.CompletedTask;
        }

        private static Task HandleAuthenticationFailed(CertificateAuthenticationFailedContext context)
        {
            return Task.CompletedTask;
        }

        private static Task HandleCertificateChallenge(CertificateChallengeContext context)
        {
            return Task.CompletedTask;
        }
    }
}