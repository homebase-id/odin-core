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
        private IPersonService _personService;
        private IPrototrialDemoDataService _prototrial;
        private IAdminIdentityAttributeService _admin;

        public DemoDataController(IPersonService personService, IPrototrialDemoDataService prototrial, IAdminIdentityAttributeService admin)
        {
            _personService = personService;
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
                var contact = new Person()
                {
                    GivenName = importedContact.GivenName,
                    Surname = importedContact.Surname,
                    PrimaryEmail = importedContact.PrimaryEmail,
                    Tag = importedContact.Tag
                };

                await _personService.Save(contact);
            }
            
            var result1 = await _prototrial.AddDigitalIdentities();

            var result2 = await _prototrial.AddConnectionRequests();
            
            return new JsonResult(result1 && result2);
        }

        [HttpGet("profiledata")]
        public async Task<IActionResult> SetProfileData()
        {
            await _prototrial.SetProfiles();
          
            var storedProfile = await _admin.GetMyProfile();
            return new JsonResult(storedProfile);
        }
    }
}
