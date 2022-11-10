using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Provisioning.Services.Registration;

namespace Youverse.Provisioning.Controllers.Registration
{
    [ApiController]
    [Route("registration")]
    public class RegistrationController : ControllerBase
    {
        private readonly IRegistrationService _regService;

        public RegistrationController(IRegistrationService regService)
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
            return new JsonResult(new NoResultResponse(true));
        }

        /// <summary>
        /// Starts a registration and returns an Id used to monitor registration progress.
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        [HttpPost]
        public async Task<Guid> StartRegistration([FromBody]RegistrationInfo info)
        {
            if (info.RequestedManagedCertificate == false)
            {
                throw new NotSupportedException("Self-managed certificates will be supported after the prototrial");
            }
            
            // var info = new RegistrationInfo()
            // {
            //     EmailAddress = "",
            //     ReservationId = reservationId,
            //     RequestedManagedCertificate = true,
            //     CertificateSigningRequest = new CertificateSigningRequest()
            //     {
            //         Locality = "",
            //         Organization = "",
            //         State = "",
            //         CommonName = "",
            //         CountryName = "",
            //         OrganizationUnit = ""
            //     }
            // };

            var registrationId = await _regService.StartRegistration(info);
            return registrationId;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetRegistrationStatus(Guid registrationId)
        {
            var status = await _regService.GetRegistrationStatus(registrationId);
            return new JsonResult(new
            {
                registrationId = registrationId,
                status = status
            });
        }

        /// <summary>
        /// Finalizes registration.  Finalization will fail if you call this before the RegistrationStatus == Complete.  You can just call it again.
        /// </summary>
        /// <param name="registrationId"></param>
        /// <returns></returns>
        [HttpGet("finalize")]
        public async Task<IActionResult> FinalizeRegistration(Guid registrationId)
        {
            await _regService.FinalizeRegistration(registrationId);
            return Ok();
        }
    }
}