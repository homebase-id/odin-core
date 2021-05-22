using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Contacts;
using DotYou.TenantHost.Security;
using DotYou.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Newtonsoft.Json;

namespace DotYou.TenantHost.Controllers.Demo
{
    [ApiController]
    [Route("api/demodata")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes =  DotYouAuthSchemes.DotIdentityOwner)]
    public class DemoDataController : ControllerBase
    {
        IContactService _contactService;

        public DemoDataController(IContactService contactService)
        {
            _contactService = contactService;
        }

        [HttpGet("contacts")]
        public async Task<IActionResult> AddContacts()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "sampledata","contacts.json");
            string json = System.IO.File.ReadAllText(path);
            var contacts = JsonConvert.DeserializeObject<ImportedContact[]>(json).ToList();

            foreach (var importedContact in contacts)
            {
                var contact = new Contact()
                {
                    GivenName = importedContact.GivenName,
                    Surname = importedContact.Surname,
                    PrimaryEmail = importedContact.PrimaryEmail,
                    Tag = importedContact.Tag
                };

                await _contactService.Save(contact);
            }

            await _contactService.Save(new Contact() { DotYouId = (DotYouIdentity)"frodobaggins.me", GivenName = "Frodo", Surname = "Baggins", Tag="Fellowship", PrimaryEmail="mail@frodobaggins.me" });
            await _contactService.Save(new Contact() { DotYouId = (DotYouIdentity)"samwisegamgee.me", GivenName = "Samwise", Surname = "Gamgee", Tag = "Fellowship", PrimaryEmail = "mail@samwisegamgee.me" });
            await _contactService.Save(new Contact() { DotYouId = (DotYouIdentity)"gandalf.middleearth.life", GivenName = "Olorin", Surname = "Maiar", Tag = "Fellowship", PrimaryEmail = "mail@gandalf.middleearth.life" });
            await _contactService.Save(new Contact() { DotYouId = (DotYouIdentity)"arwen.youfoundation.id", GivenName = "Awren", Surname = "Undomiel", Tag = "Fellowship", PrimaryEmail = "mail@arwen.youfoundation.id" });
            await _contactService.Save(new Contact() { DotYouId = (DotYouIdentity)"odin.valhalla.com", GivenName = "Odin", Surname = "", Tag = "Acquaintance", PrimaryEmail = "mail@frodobaggins.me" });

            return new JsonResult(true);
        }
    }
}
