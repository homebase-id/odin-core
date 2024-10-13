using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Verification;

namespace Odin.Hosting.Controllers.Base.Membership.Connections
{
    public abstract class CircleNetworkControllerBase(
        CircleNetworkService circleNetwork,
        TenantSystemStorage tenantSystemStorage,
        CircleNetworkVerificationService verificationService)
        : OdinControllerBase
    {
        [HttpPost("unblock")]
        public async Task<bool> Unblock([FromBody] OdinIdRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await circleNetwork.Unblock((OdinId)request.OdinId, WebOdinContext, db);
            return result;
        }

        [HttpPost("block")]
        public async Task<bool> Block([FromBody] OdinIdRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await circleNetwork.Block((OdinId)request.OdinId, WebOdinContext, db);
            return result;
        }

        [HttpPost("disconnect")]
        public async Task<bool> Disconnect([FromBody] OdinIdRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await circleNetwork.Disconnect((OdinId)request.OdinId, WebOdinContext, db);
            return result;
        }

        [HttpPost("confirm-connection")]
        public async Task<IActionResult> ConfirmConnection([FromBody] OdinIdRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            await circleNetwork.ConfirmConnection((OdinId)request.OdinId, WebOdinContext, db);
            return Ok();
        }

        [HttpPost("verify-connection")]
        public async Task<IActionResult> VerifyConnection([FromBody] OdinIdRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await verificationService.VerifyConnection((OdinId)request.OdinId, WebOdinContext, db);
            return new JsonResult(result);
        }

        [HttpPost("troubleshooting-info")]
        public async Task<IActionResult> GetReconcilableStatus([FromBody] OdinIdRequest request, bool omitContactData = true)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await circleNetwork.GetTroubleshootingInfo((OdinId)request.OdinId, WebOdinContext, db);
            return new JsonResult(result);
        }

        [HttpPost("status")]
        public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo([FromBody] OdinIdRequest request, bool omitContactData = true)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await circleNetwork.GetIcr((OdinId)request.OdinId, WebOdinContext, db);
            return result?.Redacted(omitContactData);
        }

        [HttpPost("connected")]
        public async Task<CursoredResult<long, RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, long cursor,
            bool omitContactData = false)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await circleNetwork.GetConnectedIdentities(count, cursor, WebOdinContext, db);
            return new CursoredResult<long, RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results.Select(p => p.Redacted(omitContactData)).ToList()
            };
        }

        [HttpPost("blocked")]
        public async Task<CursoredResult<long, RedactedIdentityConnectionRegistration>> GetBlockedProfiles(int count, long cursor,
            bool omitContactData = false)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await circleNetwork.GetBlockedProfiles(count, cursor, WebOdinContext, db);
            return new CursoredResult<long, RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results.Select(p => p.Redacted(omitContactData)).ToList()
            };
        }

        [HttpPost("circles/list")]
        public async Task<IEnumerable<OdinId>> GetCircleMembers([FromBody] GetCircleMembersRequest request)
        {
            var result = await circleNetwork.GetCircleMembers(request.CircleId, WebOdinContext);
            return result;
        }

        [HttpPost("circles/add")]
        public async Task<bool> GrantCircle([FromBody] AddCircleMembershipRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            await circleNetwork.GrantCircle(request.CircleId, new OdinId(request.OdinId), WebOdinContext, db);
            return true;
        }

        [HttpPost("circles/revoke")]
        public async Task<bool> RevokeCircle([FromBody] RevokeCircleMembershipRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            await circleNetwork.RevokeCircleAccess(request.CircleId, new OdinId(request.OdinId), WebOdinContext, db);
            return true;
        }
    }
}