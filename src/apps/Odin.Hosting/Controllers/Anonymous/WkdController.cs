using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Cryptography.Pgp;
using Odin.Services.Email;

namespace Odin.Hosting.Controllers.Anonymous
{
    // OpenPGP Web Key Directory (draft-koch-openpgp-webkey-service), direct method -
    // served on the tenant's own domain, so no extra DNS prefix or certificate SAN.
    // Routes in here:
    // - are accessible without authentication
    //
    // One mailbox, many names (chat-kmp EMAIL_APP.md): every address at this domain
    // shares the tenant's single encryption certificate, so the hashed-localpart
    // segment does not select between keys - any hash resolves to the one published
    // certificate, and 404 simply means email is not activated here.
    [ApiController]
    [Route(".well-known/openpgpkey")]
    public class WkdController(EmailPublicKeyService emailPublicKeyService) : ControllerBase
    {
        [HttpGet("hu/{hash}")]
        public async Task<IActionResult> GetKey(string hash)
        {
            var publishedKey = await emailPublicKeyService.GetPublishedKeyAsync();
            if (publishedKey == null)
            {
                return NotFound();
            }

            // The spec requires the binary (non-armored) form and cross-origin readability
            Response.Headers.AccessControlAllowOrigin = "*";
            var binary = OpenPgpKeyManagement.GetPublicCertificateBinary(publishedKey.PublicCertificateArmored);
            return File(binary, "application/octet-stream");
        }

        [HttpGet("policy")]
        public IActionResult GetPolicy()
        {
            // An empty policy file signals plain WKD support; some clients probe it
            // before the key lookup
            Response.Headers.AccessControlAllowOrigin = "*";
            return Content("", "text/plain");
        }
    }
}
