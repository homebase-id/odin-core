﻿using System;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Registry.Registration;
using Odin.Hosting.ApiExceptions.Client;

namespace Odin.Hosting.Controllers.Registration
{
    [ApiController]
    [Route("/api/registration/v1/registration")]
    public class RegistrationController : ControllerBase
    {
        private readonly IIdentityRegistrationService _regService;
        
        public RegistrationController(IIdentityRegistrationService regService)
        {
            _regService = regService;
        }

        /// <summary>
        /// Gets the DNS configuration required for a custom domain
        /// </summary>
        /// <returns></returns>
        [HttpGet("dns-config/{domain}")]
        public async Task<IActionResult> GetDnsConfiguration(string domain)
        {
            // SEB:TODO do proper exception handling. Errors from AssertValidDomain should come back as http 400.
            
            var dnsConfig = await _regService.GetDnsConfiguration(domain);
            return new JsonResult(dnsConfig);
        }
        
        /// <summary>
        /// Test if dns records have propageted to selection of major dns resolvers
        /// </summary>
        /// <returns></returns>
        [HttpGet("did-dns-records-propagate/{domain}")]
        public async Task<IActionResult> DidDnsRecordsPropagate(string domain)
        {
            // SEB:TODO do proper exception handling. Errors from AssertValidDomain should come back as http 400.
            
            var resolved = await _regService.ExternalDnsResolverRecordLookup(domain);
            return new JsonResult(resolved.Success);
        }
        
        /// <summary>
        /// Test domain connectivity 
        /// </summary>
        /// <returns></returns>
        [HttpGet("can-connect-to/{domain}/{port}")]
        public async Task<IActionResult> CanConnectToHostAndPort(string domain, int port)
        {
            var result = await _regService.CanConnectToHostAndPort(domain, port);
            return new JsonResult(result);
        }
        
        /// <summary>
        /// Test domain certificate 
        /// </summary>
        /// <returns></returns>
        [HttpGet("has-valid-certificate/{domain}")]
        public async Task<IActionResult> HasValidCertificate(string domain)
        {
            var result = await _regService.HasValidCertificate(domain);
            return new JsonResult(result);
        }
        
        /// <summary>
        /// Gets the list of domains managed apexes by this identity
        /// </summary>
        /// <returns></returns>
        [HttpGet("managed-domain-apexes")]
        public async Task<IActionResult> GetManagedDomainApexes()
        {
            var domains = await _regService.GetManagedDomainApexes();
            return new JsonResult(domains);
        }
        
        /// <summary>
        /// Checks availability
        /// </summary>
        /// <param name="apex"></param>
        /// <param name="prefix"></param>
        /// <returns></returns>
        [HttpGet("is-managed-domain-available/{apex}/{prefix}")]
        public async Task<IActionResult> IsManagedDomainAvailable(string prefix, string apex)
        {
            // SEB:TODO do proper exception handling. Errors from AssertValidDomain should come back as http 400.
            try
            {
                var result = await _regService.IsManagedDomainAvailable(prefix, apex);
                return new JsonResult(result);
            }
            catch (Exception)
            {
                return new JsonResult(false);
            }
        }
        
        /// <summary>
        /// Create managed domain
        /// </summary>
        /// <param name="apex"></param>
        /// <param name="prefix"></param>
        /// <returns></returns>
        [HttpPost("create-managed-domain/{apex}/{prefix}")]
        public async Task<IActionResult> CreateManagedDomain(string prefix, string apex)
        {
            // SEB:TODO do proper exception handling. Errors from AssertValidDomain should come back as http 400.
            
            var available = await _regService.IsManagedDomainAvailable(prefix, apex);
            if (!available)
            {
                return Problem(
                    statusCode: StatusCodes.Status412PreconditionFailed,
                    title: "Domain name not available"
                );
            }

            await _regService.CreateManagedDomain(prefix, apex);
            return NoContent();
        }

#if DEBUG
        /// <summary>
        /// Delete managed domain
        /// </summary>
        /// <param name="apex"></param>
        /// <param name="prefix"></param>
        /// <returns></returns>
        // curl -X DELETE https://provisioning.dotyou.cloud/api/registration/v1/registration/delete-managed-domain/demo.rocks/foo.bar
        [HttpDelete("delete-managed-domain/{apex}/{prefix}")]
        public async Task<IActionResult> DeleteManagedDomain(string prefix, string apex)
        {
            await _regService.DeleteManagedDomain(prefix, apex);
            return NoContent();
        }
#endif
        
        /// <summary>
        /// Check if own domain is available
        /// </summary>
        /// <returns></returns>
        [HttpGet("is-own-domain-available/{domain}")]
        public async Task<IActionResult> IsOwnDomainAvailable(string domain)
        {
            // SEB:TODO do proper exception handling. Errors from AssertValidDomain should come back as http 400.
            try
            {
                var result = await _regService.IsOwnDomainAvailable(domain);
                return new JsonResult(result);
            }
            catch (Exception)
            {
                return new JsonResult(false);
            }
        }
        
        /// <summary>
        /// Gets the status for the ongoing own domain registration
        /// </summary>
        /// <returns></returns>
        [HttpGet("own-domain-dns-status/{domain}")]
        public async Task<IActionResult> GetOwnDomainDnsStatus(string domain)
        {
            // SEB:TODO do proper exception handling. Errors from AssertValidDomain should come back as http 400.
            // if (string.IsNullOrWhiteSpace(domain))
            // {
            //     return Problem(
            //         statusCode: StatusCodes.Status400BadRequest,
            //         title: "Missing or invalid domain name"
            //     );
            // }
            
            var (success, dnsConfig) = await _regService.GetOwnDomainDnsStatus(domain);
            return new JsonResult(dnsConfig)
            {
                StatusCode = success ? StatusCodes.Status200OK : StatusCodes.Status202Accepted
            };
        }
        
#if DEBUG
        /// <summary>
        /// Delete own domain
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        // curl -X DELETE https://provisioning.dotyou.cloud/api/registration/v1/registration/delete-own-domain/foo.bar
        [HttpDelete("delete-own-domain/{domain}")]
        public async Task<IActionResult> DeleteOwnDomain(string domain)
        {
            await _regService.DeleteOwnDomain(domain);
            return NoContent();
        }
#endif
        
        /// <summary>
        /// Create identity on own or managed domain
        /// </summary>
        /// <param name="domain"></param>
        /// <param name="identity"></param>
        /// <returns></returns>
        [HttpPost("create-identity-on-domain/{domain}")]
        public async Task<IActionResult> CreateIdentityOnDomain(string domain, [FromBody] IdentityModel identity)
        {
            if (!MailAddress.TryCreate(identity.Email, out _))
            {
                throw new BadRequestException(message: "Invalid email address");
            }
            
            //
            // Check that our new domain has propagated to other dns resolvers
            //
            var resolved = await _regService.ExternalDnsResolverRecordLookup(domain);
            if (!resolved.Success)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "DNS records were not found by all configured external dns resolvers. Try later."
                );
            }
            
            var firstRunToken = await _regService.CreateIdentityOnDomain(domain, identity.Email, identity.PlanId);
            return new JsonResult(firstRunToken); 
        }
        
        /// <summary>
        /// Determines if the invitation code is valid
        /// </summary>
        /// <returns></returns>
        [HttpGet("is-valid-invitation-code/{code}")]
        public async Task<IActionResult> IsValidInvitationCode(string code)
        {
            var valid = await _regService.IsValidInvitationCode(code);
            if (valid)
            {
                return Ok();
            }

            return NotFound();
        }

        //
        
    }
}