using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Verification;

namespace Odin.Hosting.Controllers.Base.Membership.Connections
{
    public abstract class CircleNetworkControllerBase(
        CircleNetworkService circleNetwork,
        CircleNetworkVerificationService verificationService)
        : OdinControllerBase
    {
        [HttpPost("unblock")]
        public async Task<bool> Unblock([FromBody] OdinIdRequest request)
        {
            var result = await circleNetwork.UnblockAsync((OdinId)request.OdinId, WebOdinContext);
            return result;
        }

        [HttpPost("block")]
        public async Task<bool> Block([FromBody] OdinIdRequest request)
        {
            var result = await circleNetwork.BlockAsync((OdinId)request.OdinId, WebOdinContext);
            return result;
        }

        [HttpPost("disconnect")]
        public async Task<bool> Disconnect([FromBody] OdinIdRequest request)
        {
            var result = await circleNetwork.DisconnectAsync((OdinId)request.OdinId, WebOdinContext);
            return result;
        }

        [HttpPost("confirm-connection")]
        public async Task<IActionResult> ConfirmConnection([FromBody] OdinIdRequest request)
        {
            await circleNetwork.ConfirmConnectionAsync((OdinId)request.OdinId, WebOdinContext);
            return Ok();
        }

        [HttpPost("verify-connection")]
        public async Task<IActionResult> VerifyConnection([FromBody] OdinIdRequest request)
        {
            var result = await verificationService.VerifyConnectionAsync((OdinId)request.OdinId, HttpContext.RequestAborted,
                WebOdinContext);
            return new JsonResult(result);
        }


        [HttpPost("troubleshooting-info")]
        public async Task<IActionResult> GetReconcilableStatus([FromBody] OdinIdRequest request, bool omitContactData = true)
        {
            var result = await circleNetwork.GetTroubleshootingInfoAsync((OdinId)request.OdinId, WebOdinContext);
            return new JsonResult(result);
        }

        [HttpPost("status")]
        public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo([FromBody] OdinIdRequest request,
            bool omitContactData = true)
        {
            var result = await circleNetwork.GetIcrAsync((OdinId)request.OdinId, WebOdinContext);
            return result?.Redacted(omitContactData);
        }

        [HttpPost("connected")]
        public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, string cursor,
            bool omitContactData = false)
        {
            var result = await circleNetwork.GetConnectedIdentitiesAsync(count, Int64.Parse(cursor), WebOdinContext);
            return new CursoredResult<RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results.Select(p => p.Redacted(omitContactData)).ToList()
            };
        }

        [HttpPost("blocked")]
        public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetBlockedProfiles(int count, string cursor,
            bool omitContactData = false)
        {
            var result = await circleNetwork.GetBlockedProfilesAsync(count, Int64.Parse(cursor), WebOdinContext);
            return new CursoredResult<RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results.Select(p => p.Redacted(omitContactData)).ToList()
            };
        }

        [HttpPost("circles/list")]
        public async Task<IEnumerable<OdinId>> GetCircleMembers([FromBody] GetCircleMembersRequest request)
        {
            var result = await circleNetwork.GetCircleMembersAsync(request.CircleId, WebOdinContext);
            return result;
        }

        [HttpPost("circles/add")]
        public async Task<bool> GrantCircle([FromBody] AddCircleMembershipRequest request)
        {
            await circleNetwork.GrantCircleAsync(request.CircleId, new OdinId(request.OdinId), WebOdinContext);
            return true;
        }

        [HttpPost("circles/revoke")]
        public async Task<bool> RevokeCircle([FromBody] RevokeCircleMembershipRequest request)
        {
            await circleNetwork.RevokeCircleAccessAsync(request.CircleId, new OdinId(request.OdinId), WebOdinContext);
            return true;
        }
    }
}