using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer.SendingHost;

namespace Odin.Hosting.Controllers.Base.Transit.Specialized
{
    public class TransitQueryByGlobalTransitIdControllerBase : OdinControllerBase
    {
        private readonly TransitQueryService _transitQueryService;

        public TransitQueryByGlobalTransitIdControllerBase(TransitQueryService transitQueryService)
        {
            _transitQueryService = transitQueryService;
        }

      
    }
}