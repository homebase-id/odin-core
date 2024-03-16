using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(GuestApiPathConstants.DriveReactionsV1)]
    [AuthorizeValidGuestOrAppToken]
    public class GuestDriveReactionContentController : DriveReactionContentControllerBase
    {
        /// <summary />
        public GuestDriveReactionContentController(ReactionContentService reactionContentService) : base(reactionContentService)
        {
        }
    }
}