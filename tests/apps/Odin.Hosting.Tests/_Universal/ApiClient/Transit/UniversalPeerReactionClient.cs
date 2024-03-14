using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Transit;

public class UniversalPeerReactionClient(OdinId targetIdentity, IApiClientFactory factory)
{
    public async Task<ApiResponse<AddGroupReactionResponse>> AddGroupReaction(List<OdinId> recipients, GlobalTransitIdFileIdentifier file, string reactionContent)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerReaction>(client, ownerSharedSecret);
        var response = await svc.AddGroupReaction(new PeerAddGroupReactionRequest()
        {
            Recipients = recipients.Select(r => (string)r).ToList(),
            Request = new AddRemoteReactionRequest()
            {
                File = file,
                Reaction = reactionContent
            }
        });

        return response;
    }

    public async Task<ApiResponse<DeleteGroupReactionResponse>> DeleteGroupReaction(List<OdinId> recipients, string reaction, GlobalTransitIdFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerReaction>(client, ownerSharedSecret);
        var response = await svc.DeleteGroupReaction(new PeerDeleteGroupReactionRequest()
        {
            Recipients = recipients.Select(r => (string)r).ToList(),
            Request = new DeleteReactionRequestByGlobalTransitId
            {
                Reaction = reaction,
                File = file
            }
        });

        return response;
    }


    public async Task<ApiResponse<HttpContent>> AddReaction(string odinId, GlobalTransitIdFileIdentifier file, string reactionContent)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerReaction>(client, ownerSharedSecret);
        var response = await svc.AddReaction(new PeerAddReactionRequest()
        {
            OdinId = odinId,
            Request = new AddRemoteReactionRequest()
            {
                File = file,
                Reaction = reactionContent
            }
        });

        return response;
    }

    public async Task<ApiResponse<GetReactionsPerimeterResponse>> GetAllReactions(OdinId odinId, GlobalTransitIdFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerReaction>(client, ownerSharedSecret);
        var resp = await svc.GetAllReactions(new PeerGetReactionsRequest
        {
            OdinId = odinId,
            Request = new GetRemoteReactionsRequest
            {
                File = file,
                Cursor = 0,
                MaxRecords = 100
            }
        });

        return resp;
    }

    public async Task<ApiResponse<HttpContent>> DeleteReaction(OdinId odinId, string reaction, GlobalTransitIdFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerReaction>(client, ownerSharedSecret);
        var response = await svc.DeleteReaction(new PeerDeleteReactionRequest
        {
            OdinId = odinId,
            Request = new DeleteReactionRequestByGlobalTransitId
            {
                Reaction = reaction,
                File = file
            }
        });

        return response;
    }

    public async Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile(OdinId odinId, GlobalTransitIdFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitPeerReaction>(client, ownerSharedSecret);
        var resp = await transitSvc.DeleteAllReactionsOnFile(new PeerDeleteReactionRequest
        {
            OdinId = odinId,
            Request = new DeleteReactionRequestByGlobalTransitId()
            {
                File = file,
                Reaction = default
            }
        });

        return resp;
    }

    public async Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile(PeerGetReactionsRequest request)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerReaction>(client, ownerSharedSecret);
        var resp = await svc.GetReactionCountsByFile(request);
        return resp;
    }

    public async Task<ApiResponse<List<string>>> GetReactionsByIdentity(OdinId odinId, GlobalTransitIdFileIdentifier file)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitPeerReaction>(client, ownerSharedSecret);
        var resp = await transitSvc.GetReactionsByIdentity(new PeerGetReactionsByIdentityRequest()
        {
            Identity = odinId,
            File = file
        });

        return resp;
    }
}