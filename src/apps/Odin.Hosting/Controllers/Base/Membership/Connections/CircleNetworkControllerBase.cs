﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Controllers.Base.Membership.Connections
{
    public abstract class CircleNetworkControllerBase : OdinControllerBase
    {
        private readonly CircleNetworkService _circleNetwork;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public CircleNetworkControllerBase(CircleNetworkService db, TenantSystemStorage tenantSystemStorage)
        {
            _circleNetwork = db;
            _tenantSystemStorage = tenantSystemStorage;
        }

        [HttpPost("unblock")]
        public async Task<bool> Unblock([FromBody] OdinIdRequest request)
        {
            var result = await _circleNetwork.Unblock((OdinId)request.OdinId, WebOdinContext);
            return result;
        }

        [HttpPost("block")]
        public async Task<bool> Block([FromBody] OdinIdRequest request)
        {
            var result = await _circleNetwork.Block((OdinId)request.OdinId, WebOdinContext);
            return result;
        }

        [HttpPost("disconnect")]
        public async Task<bool> Disconnect([FromBody] OdinIdRequest request)
        {
            var result = await _circleNetwork.Disconnect((OdinId)request.OdinId, WebOdinContext);
            return result;
        }

        [HttpPost("troubleshooting-info")]
        public async Task<IActionResult> GetReconcilableStatus([FromBody] OdinIdRequest request, bool omitContactData = true)
        {
            var result = await _circleNetwork.GetTroubleshootingInfo((OdinId)request.OdinId, WebOdinContext);
            return new JsonResult(result);
        }
        
        [HttpPost("status")]
        public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo([FromBody] OdinIdRequest request, bool omitContactData = true)
        {
            var result = await _circleNetwork.GetIdentityConnectionRegistration((OdinId)request.OdinId, WebOdinContext);
            return result?.Redacted(omitContactData);
        }

        [HttpPost("connected")]
        public async Task<CursoredResult<long, RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, long cursor,
            bool omitContactData = false)
        {
            var result = await _circleNetwork.GetConnectedIdentities(count, cursor, WebOdinContext);
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
            var result = await _circleNetwork.GetBlockedProfiles(count, cursor, WebOdinContext);
            return new CursoredResult<long, RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results.Select(p => p.Redacted(omitContactData)).ToList()
            };
        }

        [HttpPost("circles/list")]
        public async Task<IEnumerable<OdinId>> GetCircleMembers([FromBody] GetCircleMembersRequest request)
        {
            var result = await _circleNetwork.GetCircleMembers(request.CircleId, WebOdinContext);
            return result;
        }

        [HttpPost("circles/add")]
        public async Task<bool> GrantCircle([FromBody] AddCircleMembershipRequest request)
        {
            await _circleNetwork.GrantCircle(request.CircleId, new OdinId(request.OdinId), WebOdinContext);
            return true;
        }

        [HttpPost("circles/revoke")]
        public async Task<bool> RevokeCircle([FromBody] RevokeCircleMembershipRequest request)
        {
            await _circleNetwork.RevokeCircleAccess(request.CircleId, new OdinId(request.OdinId), WebOdinContext);
            return true;
        }
    }
}