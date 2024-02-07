﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Core.Services.Peer.ReceivingHost;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.PeerV1 + "/app")]
    [AuthorizeValidAppToken]
    public class TransitAppController : ControllerBase
    {
        private readonly TransitInboxProcessor _transitInboxProcessor;

        public TransitAppController(TransitInboxProcessor transitInboxProcessor)
        {
            _transitInboxProcessor = transitInboxProcessor;
        }

        [HttpPost("process")]
        public async Task<InboxStatus> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            if ((request.TargetDrive?.IsValid() ?? false) == false)
            {
                throw new OdinClientException("Invalid target drive", OdinClientErrorCode.InvalidTargetDrive);
            }

            var result = await _transitInboxProcessor.ProcessInbox(request.TargetDrive, request.BatchSize);
            return result;
        }
    }
}