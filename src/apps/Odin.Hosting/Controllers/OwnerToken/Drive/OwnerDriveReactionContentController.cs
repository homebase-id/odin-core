using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveReactionsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveReactionContentController : DriveReactionContentControllerBase
    {
        /// <summary />
        public OwnerDriveReactionContentController(ReactionContentService reactionContentService) : base(reactionContentService)
        {
        }
    }
}