﻿using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle;

namespace DotYou.Types.ApiClient
{
    public interface ICircleNetworkRequestsClient
    {
        private const string RootPath = "/api/circlenetwork/requests";
        private const string SentPathRoot = RootPath + "/sent";
        private const string PendingPathRoot = RootPath + "/pending";

        [Post(SentPathRoot)]
        Task<ApiResponse<NoResultResponse>> SendConnectionRequest([Body] ConnectionRequestHeader requestHeader);

        [Post(PendingPathRoot + "/accept/{dotYouId}")]
        Task<ApiResponse<NoResultResponse>> AcceptConnectionRequest(string dotYouId);

        [Get(SentPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequest>>> GetSentRequestList([Query] PageOptions pageRequest);

        [Get(SentPathRoot + "/{recipientDotYouId}")]
        Task<ApiResponse<ConnectionRequest>> GetSentRequest(string recipientDotYouId);
        
        
        [Delete(SentPathRoot + "/{recipientDotYouId}")]
        Task<ApiResponse<NoResultResponse>> DeleteSentRequest(string recipientDotYouId);

        [Get(PendingPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequest>>> GetPendingRequestList([Query] PageOptions pageRequest);

        [Get(PendingPathRoot + "/{senderDotYouId}")]
        Task<ApiResponse<ConnectionRequest>> GetPendingRequest(string senderDotYouId);

        [Delete(PendingPathRoot + "/{senderDotYouId}")]
        Task<ApiResponse<NoResultResponse>> DeletePendingRequest(string senderDotYouId);
    }
}