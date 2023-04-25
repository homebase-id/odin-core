using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Registry.Registration;

namespace Youverse.Hosting.Controllers.Registration
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
        [HttpGet("dns")]
        public async Task<IActionResult> GetDnsConfiguration([FromQuery] string domain)
        {
            // SEB:TODO do proper domain name validation
            if (string.IsNullOrWhiteSpace(domain))
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Missing or invalid domain name"
                );
            }
            
            var dnsConfig = await _regService.GetDnsConfiguration(domain);
            return new JsonResult(dnsConfig.AllDnsRecords);
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
            
            var result = await _regService.IsManagedDomainAvailable(prefix, apex);
            
            return new JsonResult(result);
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
           
            return new JsonResult("ok"); // SEB:TODO
        }


        /// <summary>
        /// Creates a reservation
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("reservations")]
        public async Task<IActionResult> Reserve([FromBody] ReservationRequest request)
        {
            var result = await _regService.Reserve(request);
            return new JsonResult(result);
        }

        /// <summary>
        /// Cancels an existing reservation
        /// </summary>
        /// <param name="reservationId"></param>
        /// <returns></returns>
        [HttpDelete("reservations/{reservationId}")]
        public async Task<IActionResult> CancelReservation(Guid reservationId)
        {
            await _regService.CancelReservation(reservationId);
            return new JsonResult(true);
        }

        // /// <summary>
        // /// Starts a registration and returns an Id used to monitor registration progress.
        // /// </summary>
        // /// <param name="info"></param>
        // /// <returns></returns>
        // /// <exception cref="NotSupportedException"></exception>
        // /// SEB:TODO remove this
        // [HttpPost("register")]
        // public async Task<Guid> StartRegistration([FromBody] RegistrationInfo info)
        // {
        //     var registrationId = await _regService.StartRegistration(info);
        //     return registrationId;
        // }
        
        /// <summary>
        /// Gets the status for the ongoing own domain registration
        /// </summary>
        /// <returns></returns>
        [HttpGet("register-own-domain-status")]
        public async Task<IActionResult> GetOwnDomainRegistrationStatus(string domain)
        {
            // SEB:TODO do proper domain name validationz
            if (string.IsNullOrWhiteSpace(domain))
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Missing or invalid domain name"
                );
            }
            
            var (success, dnsConfig) = await _regService.VerifyOwnDomain(domain);
            return new JsonResult(dnsConfig.AllDnsRecords)
            {
                StatusCode = success ? StatusCodes.Status200OK : StatusCodes.Status202Accepted
            };
        }

        // /// <summary>
        // /// Gets the status for the ongoing registration
        // /// </summary>
        // /// <param name="firstRunToken"></param>
        // /// <returns></returns>
        // [HttpGet("status")]
        // public async Task<IActionResult> GetStatus(Guid firstRunToken)
        // {
        //     var status = await _regService.GetRegistrationStatus(firstRunToken);
        //     return new JsonResult(status);
        // }
        
        // /// <summary>
        // /// Marks registration for an identity complete
        // /// </summary>
        // /// <returns></returns>
        // [HttpGet("finalize")]
        // public async Task<IActionResult> Finalize(Guid frid)
        // {
        //     try
        //     {
        //         await _regService.FinalizeRegistration(frid);
        //     }
        //     catch (Exception e)
        //     {
        //     }
        //
        //     return Ok();
        // }
        
           
        
    }
}