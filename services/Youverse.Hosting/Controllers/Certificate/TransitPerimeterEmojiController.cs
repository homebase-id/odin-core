using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Authentication.Perimeter;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/host/reactions")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.FollowerCertificateAuthScheme)]
    public class TransitPerimeterEmojiController : OdinControllerBase
    {
        private readonly TransitEmojiPerimeterService _emojiPerimeterService;

        public TransitPerimeterEmojiController(TransitEmojiPerimeterService emojiPerimeterService)
        {
            _emojiPerimeterService = emojiPerimeterService;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddEmojiReaction(SharedSecretEncryptedTransitPayload payload)
        {
            await _emojiPerimeterService.AddReaction(payload);
            return NoContent();
        }

        [HttpPost("list")]
        public async Task<GetReactionsResponse> GetAllReactions(SharedSecretEncryptedTransitPayload payload)
        {
            return await _emojiPerimeterService.GetReactions(payload);
        }

        
        // [HttpPost("delete")]
        // public async Task<IActionResult> DeleteEmojiReaction(SharedSecretEncryptedTransitPayload payload)
        // {
        //     await _emojiPerimeterService.DeleteReaction(payload);
        //     return NoContent();
        // }
    }
}