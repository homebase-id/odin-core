using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Configuration.VersionUpgrade.Version2tov3
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V2ToV3VersionMigrationService(
        ILogger<V2ToV3VersionMigrationService> logger,
        CircleNetworkRequestService circleNetworkRequestService,
        CircleNetworkIntroductionService introductionService)
    {
        private readonly UnixTimeUtc _maxDate = UnixTimeUtc.FromDateTime(new DateTime(2025, 1, 6));

        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            //
            // delete all old introductions for sanity
            //
            await introductionService.DeleteIntroductionsAsync(odinContext, _maxDate);

            //
            // delete all outgoing connection requests that were due to an introduction
            //
            var outgoingRequests = await circleNetworkRequestService.GetSentRequestsAsync(PageOptions.All, odinContext);
            var introductoryOutgoingRequests = outgoingRequests.Results.Where(o =>
                o.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction &&
                o.ReceivedTimestampMilliseconds < _maxDate);
            foreach (var request in introductoryOutgoingRequests)
            {
                await circleNetworkRequestService.DeleteSentRequest((OdinId)request.Recipient, odinContext);
            }

            //
            // delete all incoming connection requests that were due to an introduction
            //
            var incomingRequests = await circleNetworkRequestService.GetPendingRequestsAsync(PageOptions.All, odinContext);
            foreach (var r in incomingRequests.Results.Where(i => i.ReceivedTimestampMilliseconds < _maxDate))
            {
                try
                {
                    // Note: it could fail to decrypt given the change in the key type, so we capture just in case
                    var incomingRequest = await circleNetworkRequestService.GetPendingRequestAsync(r.SenderOdinId, odinContext);
                    if (incomingRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
                    {
                        await circleNetworkRequestService.DeletePendingRequest(r.SenderOdinId, odinContext);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to delete introductory request from {sender}", r.SenderOdinId);
                }
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var introductions = await introductionService.GetReceivedIntroductionsAsync(odinContext);
            if (introductions.Any(intro => intro.Received < _maxDate))
            {
                throw new OdinSystemException($"Validation failed: there is one or more introductions before {_maxDate.ToDateTime().ToShortDateString()}");
            }

            var outgoingRequests = await circleNetworkRequestService.GetSentRequestsAsync(PageOptions.All, odinContext);
            var hasOutgoingIntroductory =
                outgoingRequests.Results.Any(o => o.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
            if (hasOutgoingIntroductory)
            {
                throw new OdinSystemException($"Validation failed: there is one or more outgoing-introductory requests before {_maxDate.ToDateTime().ToShortDateString()}");
            }

            var incomingRequests = await circleNetworkRequestService.GetPendingRequestsAsync(PageOptions.All, odinContext);
            foreach (var r in incomingRequests.Results.Where(i => i.ReceivedTimestampMilliseconds < _maxDate))
            {
                try
                {
                    var incomingRequest = await circleNetworkRequestService.GetPendingRequestAsync(r.SenderOdinId, odinContext);
                    if (incomingRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
                    {
                        throw new OdinSystemException($"Validation failed: there is one or more incoming-introductory requests before {_maxDate.ToDateTime().ToShortDateString()}");
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to delete introductory request from {sender}", r.SenderOdinId);
                }
            }
        }
    }
}