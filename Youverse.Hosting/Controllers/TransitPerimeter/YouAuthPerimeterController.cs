using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication.YouAuth;

#nullable enable
namespace Youverse.Hosting.Controllers.TransitPerimeter
{
    [ApiController]
    [Route("/api/perimeter/youauth")]
    public class YouAuthPerimeterController : Controller
    {
        private readonly IYouAuthService _youAuthService;

        public YouAuthPerimeterController(IYouAuthService youAuthService)
        {
            _youAuthService = youAuthService;
        }
        
        [HttpGet("validate-ac-res")]
        [Produces("application/json")]
        public async Task<ActionResult> ValidateAuthorizationCodeResponse(
            [FromQuery(Name = YouAuthDefaults.Initiator)]
            string initiator,
            [FromQuery(Name = YouAuthDefaults.AuthorizationCode)]
            string authorizationCode)
        {
            var (success, remoteGrantKey) = await _youAuthService.ValidateAuthorizationCode(initiator, authorizationCode);

            if (success)
            {
                return new ObjectResult(remoteGrantKey);
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