using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Configuration;
using Odin.Services.Email;

namespace Odin.Hosting.Controllers.Anonymous
{
    // Thunderbird-style mail autoconfig (docs/email-keys-plan.md "Client access").
    // Routes in here:
    // - are accessible without authentication
    //
    // 404 until tenant mail is enabled for the environment AND this tenant's mail is
    // ready (published key, or a provisioned standard mailbox) - clients treat 404 as
    // "no autoconfig, ask the user", so inertness is free.
    [ApiController]
    [Route(".well-known/autoconfig/mail/config-v1.1.xml")]
    public class MailAutoconfigController(
        OdinConfiguration configuration,
        EmailSetupStateService emailSetupStateService,
        EmailPublicKeyService emailPublicKeyService) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAutoconfig()
        {
            var tenantMail = configuration.Email.TenantMail;
            if (!tenantMail.Enabled || tenantMail.MxNodes.Count == 0)
            {
                return NotFound();
            }

            var setup = await emailSetupStateService.GetAsync();
            if (!EmailSetupStateService.IsMailReady(setup, await emailPublicKeyService.GetPublishedKeyAsync()))
            {
                return NotFound();
            }

            var domain = Request.Host.Host;
            return Content(MailAutoconfig.BuildXml(domain, tenantMail.MxNodes), "application/xml");
        }
    }
}
