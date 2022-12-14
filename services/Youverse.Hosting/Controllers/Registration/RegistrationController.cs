using System;
using System.Net;
using System.Threading.Tasks;
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
        /// Checks availability
        /// </summary>
        /// <param name="domainName"></param>
        /// <returns></returns>
        [HttpGet("availability/{domainName}")]
        public async Task<IActionResult> IsAvailable(string domainName)
        {
            var result = await _regService.IsAvailable(domainName);
            return new JsonResult(result);
        }

        /// <summary>
        /// Gets the list of domains managed by this identity
        /// </summary>
        /// <returns></returns>
        [HttpGet("domains")]
        public async Task<IActionResult> GetManagedDomains()
        {
            var domains = await _regService.GetManagedDomains();
            return new JsonResult(domains);
        }

        /// <summary>
        /// Gets the DNS configuration required for a custom domain
        /// </summary>
        /// <returns></returns>
        [HttpGet("dns")]
        public async Task<IActionResult> GetDnsConfiguration(string domain)
        {
            var dnsConfig = await _regService.GetDnsConfiguration(domain);
            return new JsonResult(dnsConfig);
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

        /// <summary>
        /// Starts a registration and returns an Id used to monitor registration progress.
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        [HttpPost("register")]
        public async Task<Guid> StartRegistration([FromBody] RegistrationInfo info)
        {
            var registrationId = await _regService.StartRegistration(info);
            return registrationId;
        }

        /// <summary>
        /// Finalizes registration.  Finalization will fail if you call this before the RegistrationStatus == Complete.  You can just call it again.
        /// </summary>
        /// <param name="firstRunToken"></param>
        /// <returns></returns>
        [HttpGet("finalize")]
        public async Task<IActionResult> FinalizeRegistration(Guid firstRunToken)
        {
            var status = await _regService.GetRegistrationStatus(firstRunToken);

            if (status != RegistrationStatus.ReadyForPassword)
            {
                throw new YouverseClientException("Cannot finalize pending registration", YouverseClientErrorCode.RegistrationStatusNotReadyForFinalization);
            }
            
            await _regService.FinalizeRegistration(firstRunToken);
            return Ok();
        }
        
        /// <summary>
        /// Gets the status for the ongoing registration
        /// </summary>
        /// <param name="firstRunToken"></param>
        /// <returns></returns>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus(Guid firstRunToken)
        {
            var status = await _regService.GetRegistrationStatus(firstRunToken);
            return new JsonResult(status);
        }
    }
}