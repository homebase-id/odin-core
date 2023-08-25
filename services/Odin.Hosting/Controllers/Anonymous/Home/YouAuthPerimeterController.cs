#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Hosting.Controllers.Home;

namespace Odin.Hosting.Controllers.Anonymous.Home
{
    [ApiController]
    [Route("/api/perimeter/youauth")]
    [Obsolete("SEB:TODO delete me")]
    public class YouAuthPerimeterController : Controller
    {
        private readonly HomeAuthenticatorService _homeAuthenticatorService;

        public YouAuthPerimeterController(HomeAuthenticatorService homeAuthenticatorService)
        {
            _homeAuthenticatorService = homeAuthenticatorService;
        }
        
        [HttpGet("validate-ac-res")]
        [Produces("application/json")]
        public async Task<ActionResult> ValidateAuthorizationCodeResponse(
            [FromQuery(Name = HomeApiPathConstants.Initiator)]
            string initiator,
            [FromQuery(Name = HomeApiPathConstants.AuthorizationCode)]
            string authorizationCode)
        {
            var (success, clientAuthTokenBytes) = await _homeAuthenticatorService.ValidateAuthorizationCode(initiator, authorizationCode);

            if (success)
            {
                return new ObjectResult(clientAuthTokenBytes);
            }

            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid code",
                Instance = HttpContext.Request.Path
            };
            return new ObjectResult(problemDetails)
            {
                ContentTypes = {"application/problem+json"},
                StatusCode = problemDetails.Status,
            };
        }
    }
}