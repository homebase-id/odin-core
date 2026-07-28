using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Contacts;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Contacts;

namespace Odin.Hosting.Controllers.OwnerToken.Contacts
{
    [ApiController]
    [Route(OwnerApiPathConstants.ContactsV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerContactsController(
        ContactService contactService,
        ContactEnrichmentService contactEnrichmentService,
        IAppRegistrationService appRegistrationService)
        : ContactsControllerBase(contactService, contactEnrichmentService, appRegistrationService);
}
