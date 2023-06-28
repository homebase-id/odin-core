﻿using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Contacts.Circle.Requests;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Circles
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/requests")]
    [AuthorizeValidOwnerToken]
    public class CircleNetworkRequestsController : ControllerBase
    {
        readonly CircleNetworkRequestService _requestService;

        public CircleNetworkRequestsController(CircleNetworkRequestService cn)
        {
            _requestService = cn;
        }

        /// <summary>
        /// Gets a list of connection requests that are awaiting a response
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpGet("pending/list")]
        public async Task<PagedResult<PendingConnectionRequestHeader>> GetPendingRequestList(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            return result;
            // var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            // return new PagedResult<PendingConnectionRequestHeader>(result.Request, result.TotalPages, resp);
        }

        /// <summary>
        /// Gets a connection request by sender that is awaiting a response from the recipient
        /// </summary>
        /// <param name="sender"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending/single")]
        public async Task<ConnectionRequestResponse> GetPendingRequest([FromBody] OdinIdRequest sender)
        {
            var result = await _requestService.GetPendingRequest((OdinId)sender.OdinId);

            if (result == null)
            {
                this.HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return null;
            }

            return ConnectionRequestResponse.FromConnectionRequest(result);
        }

        /// <summary>
        /// Accepts a pending connection request
        /// </summary>
        /// <param name="header"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending/accept")]
        public async Task<bool> AcceptConnectionRequest([FromBody] AcceptRequestHeader header)
        {
            await _requestService.AcceptConnectionRequest(header);
            return true;
        }

        /// <summary>
        /// Deletes a pending connection request
        /// </summary>
        /// <param name="sender"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending/delete")]
        public async Task<bool> DeletePendingRequest([FromBody] OdinIdRequest sender)
        {
            await _requestService.DeletePendingRequest((OdinId)sender.OdinId);
            return true;
        }

        /// <summary>
        /// Gets a list of sent connection requests
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpGet("sent/list")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetSentRequestList(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetSentRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }

        /// <summary>
        /// Gets a sent connection request by recipient
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sent/single")]
        public async Task<ConnectionRequestResponse> GetSentRequest([FromBody] OdinIdRequest recipient)
        {
            var result = await _requestService.GetSentRequest((OdinId)recipient.OdinId);
            if (result == null)
            {
                this.HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return null;
            }

            return ConnectionRequestResponse.FromConnectionRequest(result);
        }

        /// <summary>
        /// Deletes a connection request sent to the specified recipient.
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sent/delete")]
        public async Task<bool> DeleteSentRequest([FromBody] OdinIdRequest recipient)
        {
            await _requestService.DeleteSentRequest((OdinId)recipient.OdinId);
            return true;
        }

        /// <summary>
        /// Sends a connection request.
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sendrequest")]
        public async Task<bool> SendConnectionRequest([FromBody] ConnectionRequestHeader requestHeader)
        {
            await _requestService.SendConnectionRequest(requestHeader);
            return true;
        }
    }
}