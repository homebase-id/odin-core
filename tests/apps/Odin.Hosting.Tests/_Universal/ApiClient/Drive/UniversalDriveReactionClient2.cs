using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.Reactions.DTOs;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive;

public class UniversalDriveReactionClient2(OdinId targetIdentity, IApiClientFactory factory)
{
    public async Task<ApiResponse<GetReactionsResponse2>> GetReactions(
        TestIdentity author,
        ExternalFileIdentifier file,
        GlobalTransitIdFileIdentifier globalTransitIdFileIdentifier)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient2>(client, ownerSharedSecret);
        var request = new GetReactionsRequest2
        {
            AuthorOdinId = author.OdinId,
            TargetDrive = file.TargetDrive,
            FileId = file.FileId,
            GlobalTransitId = globalTransitIdFileIdentifier.GlobalTransitId,
            Cursor = 0,
            MaxRecords = int.MaxValue
        };
        var response = await svc.GetReactions(request);
        return response;
    }

    //

    public async Task<ApiResponse<HttpContent>> AddReaction(
        TestIdentity author,
        ExternalFileIdentifier file,
        GlobalTransitIdFileIdentifier globalTransitIdFileIdentifier,
        string reaction)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);

        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient2>(client, ownerSharedSecret);
        var response = await svc.AddReaction(new AddReactionRequest2
        {
            AuthorOdinId = author.OdinId,
            FileId = file.FileId,
            TargetDrive = file.TargetDrive,
            GlobalTransitId = globalTransitIdFileIdentifier.GlobalTransitId,
            Reaction = reaction
        });

        return response;
    }

    //

    public async Task<ApiResponse<HttpContent>> DeleteReaction(
        TestIdentity author,
        ExternalFileIdentifier file,
        GlobalTransitIdFileIdentifier globalTransitIdFileIdentifier,
        string reaction)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient2>(client, ownerSharedSecret);
        var response = await svc.DeleteReaction(new DeleteReactionRequest2()
        {
            AuthorOdinId = author.OdinId,
            FileId = file.FileId,
            TargetDrive = file.TargetDrive,
            GlobalTransitId = globalTransitIdFileIdentifier.GlobalTransitId,
            Reaction = reaction
        });

        return response;
    }

    //

    public async Task<ApiResponse<HttpContent>> DeleteAllReactions(
        TestIdentity author,
        ExternalFileIdentifier file,
        GlobalTransitIdFileIdentifier globalTransitIdFileIdentifier)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient2>(client, ownerSharedSecret);
        var response = await svc.DeleteAllReactions(new DeleteAllReactionsRequest2
        {
            AuthorOdinId = author.OdinId,
            FileId = file.FileId,
            GlobalTransitId = globalTransitIdFileIdentifier.GlobalTransitId,
            TargetDrive = file.TargetDrive,
        });

        return response;
    }

    //

    public async Task<ApiResponse<GetReactionCountsResponse2>> GetReactionCountsByFile(
        TestIdentity author,
        ExternalFileIdentifier file,
        GlobalTransitIdFileIdentifier globalTransitIdFileIdentifier)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient2>(client, ownerSharedSecret);
        var request = new GetReactionsRequest2
        {
            AuthorOdinId = author.OdinId,
            TargetDrive = file.TargetDrive,
            FileId = file.FileId,
            GlobalTransitId = globalTransitIdFileIdentifier.GlobalTransitId,
            Cursor = 0,
            MaxRecords = Int32.MaxValue
        };

        var response = await svc.GetReactionCountsByFile(request);
        return response;
    }

    //

    public async Task<ApiResponse<List<string>>> GetReactionsByIdentity(
        TestIdentity author,
        OdinId identity,
        ExternalFileIdentifier file,
        GlobalTransitIdFileIdentifier globalTransitIdFileIdentifier)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveReactionHttpClient2>(client, ownerSharedSecret);
        var response = await svc.GetReactionsByIdentity(new GetReactionsByIdentityRequest2
        {
            Identity = identity,
            AuthorOdinId = author.OdinId,
            TargetDrive = file.TargetDrive,
            FileId = file.FileId,
            GlobalTransitId = globalTransitIdFileIdentifier.GlobalTransitId,
            Cursor = 0,
            MaxRecords = int.MaxValue
        });

        return response;
    }
}