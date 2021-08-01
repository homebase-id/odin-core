using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Admin.IdentityManagement;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Contacts;
using DotYou.Kernel.Services.Demo;
using DotYou.TenantHost.Security;
using DotYou.Types;
using DotYou.Types.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Newtonsoft.Json;

namespace DotYou.TenantHost.Controllers.Demo
{
    [ApiController]
    [Route("api/demodata")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes =  DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class DemoDataController : ControllerBase
    {
        private IContactService _contactService;
        private IPrototrialDemoDataService _prototrial;
        private IAdminIdentityAttributeService _admin;

        public DemoDataController(IContactService contactService, IPrototrialDemoDataService prototrial, IAdminIdentityAttributeService admin)
        {
            _contactService = contactService;
            _prototrial = prototrial;
            _admin = admin;
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
            
            var result = await _prototrial.AddDigitalIdentities();
            return new JsonResult(result);
        }

        [HttpGet("profiledata")]
        public async Task<IActionResult> SetProfileData()
        {
            await _prototrial.SetPublicProfile();
            var storedProfile = await _admin.GetPublicProfile();
            return new JsonResult(storedProfile);
        }
    }
}
