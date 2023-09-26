﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Transit;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.TransitSenderV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerTransitSenderController : TransitSenderControllerBase
    {
        public OwnerTransitSenderController(ITransitService transitService) : base(transitService)
        {
        }
    }
}