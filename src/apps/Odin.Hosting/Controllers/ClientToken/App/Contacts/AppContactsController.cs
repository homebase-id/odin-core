using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Contacts;
using Odin.Services.Authorization.Apps;
using Odin.Services.Contacts;

namespace Odin.Hosting.Controllers.ClientToken.App.Contacts
{
    [ApiController]
    [Route(AppApiPathConstantsV1.ContactsV1)]
    [AuthorizeValidAppToken]
    public class AppContactsController(
        ContactService contactService,
        ContactEnrichmentService contactEnrichmentService,
        IAppRegistrationService appRegistrationService)
        : ContactsControllerBase(contactService, contactEnrichmentService, appRegistrationService);
}
