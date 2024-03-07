using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Peer.Incoming.Drive.Transfer;
using Odin.Core.Services.Peer.Incoming.Reactions;
using Odin.Core.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Transit;

public class UniversalTransitApiClient(OdinId targetIdentity, IApiClientFactory factory, Guid ownerApiSystemProcessApiKey)
{
    public async Task ProcessOutbox(int batchSize = 1)
    {
        var client = factory.CreateHttpClient(targetIdentity, out _);
        {
            var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            client.DefaultRequestHeaders.Add(SystemAuthConstants.Header, ownerApiSystemProcessApiKey.ToString());
            var resp = await transitSvc.ProcessOutbox(batchSize);
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task ProcessInbox(TargetDrive drive)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = drive });
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task<ApiResponse<HttpContent>> AddReaction(TestIdentity recipient, GlobalTransitIdFileIdentifier file, string reactionContent)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitOwnerTransitReaction>(client, ownerSharedSecret);
        var response = await transitSvc.AddReaction(new TransitAddReactionRequest()
        {
            OdinId = recipient.OdinId,
            Request = new AddRemoteReactionRequest()
            {
                File = file,
                Reaction = reactionContent
            }
        });

        return response;
    }

    public async Task<ApiResponse<GetReactionsPerimeterResponse>> GetAllReactions(TestIdentity recipient, GetRemoteReactionsRequest request)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitOwnerTransitReaction>(client, ownerSharedSecret);
        var resp = await transitSvc.GetAllReactions(new TransitGetReactionsRequest()
        {
            OdinId = recipient.OdinId,
            Request = request
        });

        return resp;
    }

    public async Task<ApiResponse<HttpContent>> DeleteReaction(TestIdentity recipient, string reaction, GlobalTransitIdFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitOwnerTransitReaction>(client, ownerSharedSecret);
        var response = await transitSvc.DeleteReactionContent(new TransitDeleteReactionRequest()
        {
            OdinId = recipient.OdinId,
            Request = new DeleteReactionRequestByGlobalTransitId()
            {
                Reaction = reaction,
                File = file
            }
        });

        return response;
    }

    public async Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile(TestIdentity recipient, GlobalTransitIdFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitOwnerTransitReaction>(client, ownerSharedSecret);
        var resp = await transitSvc.DeleteAllReactionsOnFile(new TransitDeleteReactionRequest()
        {
            OdinId = recipient.OdinId,
            Request = new DeleteReactionRequestByGlobalTransitId()
            {
                Reaction = "",
                File = file
            }
        });

        return resp;
    }

    public async Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile(TestIdentity recipient, GetRemoteReactionsRequest request)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitOwnerTransitReaction>(client, ownerSharedSecret);
        var resp = await transitSvc.GetReactionCountsByFile(new TransitGetReactionsRequest()
        {
            OdinId = recipient.OdinId,
            Request = request
        });

        return resp;
    }

    public async Task<List<string>> GetReactionsByIdentity(TestIdentity recipient, OdinId identity1, GlobalTransitIdFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitOwnerTransitReaction>(client, ownerSharedSecret);
        var resp = await transitSvc.GetReactionsByIdentity(new TransitGetReactionsByIdentityRequest()
        {
            OdinId = recipient.OdinId,
            Identity = identity1,
            File = file
        });

        return resp.Content;
    }
}