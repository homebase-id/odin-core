using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.OwnerApi.Drive;
using Youverse.Hosting.Tests.OwnerApi.Transit.Emoji;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient;

public class TransitApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public TransitApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task ProcessOutbox(int batchSize = 1)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
            var resp = await transitSvc.ProcessOutbox(batchSize);
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task ProcessIncomingInstructionSet(TargetDrive drive)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.ProcessIncomingInstructions(new ProcessTransitInstructionRequest() { TargetDrive = drive });
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task AddReaction(TestIdentity recipient, GlobalTransitIdFileIdentifier file, string reactionContent)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitEmojiHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.AddReaction(new TransitAddReactionRequest()
            {
                OdinId = recipient.OdinId,
                Request = new AddRemoteReactionRequest()
                {
                    File = file,
                    Reaction = reactionContent
                }
            });

            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task<GetReactionsResponse> GetAllReactions(TestIdentity recipient, GetRemoteReactionsRequest request)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitEmojiHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.GetAllReactions(new TransitGetReactionsRequest()
            {
                OdinId = recipient.OdinId,
                Request = request
            });

            return resp.Content;
        }
    }
}