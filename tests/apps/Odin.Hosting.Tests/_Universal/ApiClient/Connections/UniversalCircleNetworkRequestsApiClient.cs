using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Connections;

public class UniversalCircleNetworkRequestsApiClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> AcceptConnectionRequest(OdinId sender, IEnumerable<GuidId> circleIdsGrantedToSender = null)
    {
        // Accept the request
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);

            var header = new AcceptRequestHeader()
            {
                Sender = sender,
                CircleIds = circleIdsGrantedToSender,
                ContactData = new ContactRequestData()
            };

            var acceptResponse = await svc.AcceptConnectionRequest(header);
            return acceptResponse;
        }
    }

    public async Task<ApiResponse<ConnectionRequestResponse>> GetIncomingRequestFrom(OdinId sender)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);

            var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender });

            return response;
        }
    }

    public async Task<ApiResponse<ConnectionRequestResponse>> GetOutgoingSentRequestTo(OdinId recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);

        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);

            var response = await svc.GetSentRequest(new OdinIdRequest() { OdinId = recipient });

            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteConnectionRequestFrom(OdinId sender)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);

        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = sender });
            return deleteResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteSentRequestTo(OdinId recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);

        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = recipient });
            return deleteResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> SendConnectionRequest(OdinId recipient,
        IEnumerable<GuidId> circlesGrantedToRecipient = null)
    {
        // Send the request
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);

        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);

            var id = Guid.NewGuid();
            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = recipient,
                Message = "Please add me",
                ContactData = new ContactRequestData()
                {
                    Name = "Test Test"
                },
                CircleIds = circlesGrantedToRecipient?.ToList()
            };

            var response = await svc.SendConnectionRequest(requestHeader);
            return response;
        }
    }

    public async Task<ApiResponse<IntroductionResult>> SendIntroductions(IntroductionGroup group)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);
            return await svc.SendIntroductions(group);
        }
    }

    public async Task<ApiResponse<HttpContent>> ProcessIncomingIntroductions()
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);
            return await svc.ProcessIncomingIntroductions();
        }
    }

    public async Task<ApiResponse<HttpContent>> AutoAcceptEligibleIntroductions()
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);
            return await svc.AutoAcceptEligibleIntroductions();
        }
    }

    public async Task<ApiResponse<List<IdentityIntroduction>>> GetReceivedIntroductions()
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);
            return await svc.GetReceivedIntroductions();
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteAllIntroductions()
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, ownerSharedSecret);
            return await svc.DeleteAllIntroductions();
        }
    }

    public async Task<ApiResponse<HttpContent>> DisconnectFrom(OdinId recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var disconnectResponse = await RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret)
                .Disconnect(new OdinIdRequest() { OdinId = recipient });
            return disconnectResponse;
        }
    }

    public async Task<TimeSpan> AwaitIntroductionsProcessing(TimeSpan? maxWaitTime = null)
    {
        //
        // Note: this checks the transient temp drive since it's what we hacked in for the outbox sending
        //
        
        var maxWait = maxWaitTime ?? TimeSpan.FromSeconds(40);

        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);

        var drive = SystemDriveConstants.TransientTempDrive;
        
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var response = await svc.GetDriveStatus(drive.Alias, drive.Type);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Error occured while retrieving outbox status");
            }

            var status = response.Content;
            if (status.Outbox.TotalItems == 0)
            {
                return sw.Elapsed;
            }

            if (sw.Elapsed > maxWait)
            {
                throw new TimeoutException(
                    $"timeout occured while waiting for outbox to complete processing " +
                    $"(wait time: {maxWait.TotalSeconds}sec. " +
                    $"Total Items: {status.Outbox.TotalItems} " +
                    $"Checked Out {status.Outbox.CheckedOutCount})");
            }

            await Task.Delay(100);
        }
    }
}