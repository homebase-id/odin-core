using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Files;
using Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Query;
using Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Reactions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// What the four ported <c>AppAPI/Transit</c> fixtures shared through <c>AppApiClient</c>: an app
/// registered over a fresh drive, and the app-scoped transit query / reaction / file-sender Refit
/// surfaces reached as that app.
/// </summary>
/// <remarks>
/// The V1 originals each carried a private <c>CreateAppAndClient(identity, params int[] permissionKeys)</c>
/// — four byte-identical copies — and then went through <c>AppApiClient.TransitQuery</c> /
/// <c>.TransitReactionSender</c> / <c>.TransitFileSender</c>. Those client classes are built on
/// <c>OwnerApiTestUtils</c> and cannot be reached from the fast host, so the Refit interfaces they
/// wrap are used directly here. Their routes are absolute (<c>/api/apps/v1/transit/...</c>), so they
/// pass through the factory's path normalizer untouched.
/// <para>
/// <see cref="QueryFor"/> and <see cref="SenderFor"/> take a <see cref="FileSystemType"/> because the
/// V1 clients set it per call as a request header, which
/// <see cref="V2CallerExtensions.RefitFor{T}"/> has no parameter for — the one framework gap this
/// batch hit. Kept local rather than widening <c>RefitFor</c>, since nothing outside this folder
/// needs it yet.
/// </para>
/// </remarks>
internal static class AppTransitClients
{
    /// <summary>
    /// The originals' <c>CreateAppAndClient</c>: a fresh non-anonymous drive plus an app holding
    /// <see cref="DrivePermission.All"/> on it and the given permission keys.
    /// </summary>
    public static async Task<AppSession> CreateAppAsync(OwnerSession owner, params int[] permissionKeys)
    {
        var appDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(appDrive, "Some Drive 1", allowAnonymousReads: false);
        return await AppSession.SetupAsync(owner, appDrive, DrivePermission.All, permissionKeys);
    }

    /// <summary>The app's transit-query surface, scoped to <paramref name="fileSystemType"/>.</summary>
    public static IRefitAppTransitQuery QueryFor(AppSession app, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var http = app.Factory.CreateHttpClient(app.Identity, out var sharedSecret, fileSystemType);
        return RefitCreator.RestServiceFor<IRefitAppTransitQuery>(http, sharedSecret);
    }

    /// <summary>The app's transit reaction-sender surface.</summary>
    public static IRefitAppTransitReactionSender ReactionsFor(AppSession app)
    {
        var http = app.Factory.CreateHttpClient(app.Identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(http, sharedSecret);
    }

    /// <summary>
    /// <c>AppTransitSenderApiClient.TransferFile</c> for the no-payload case — the only one either
    /// caller in this folder uses. The metadata is forced unencrypted and the key header is still
    /// sent, exactly as the original client did.
    /// </summary>
    public static async Task<ApiResponse<TransitResult>> TransferFileAsync(
        AppSession app,
        UploadFileMetadata fileMetadata,
        List<string> recipients,
        TargetDrive remoteTargetDrive,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        var instructionSet = new TransitInstructionSet
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = null,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients,
            Manifest = new UploadManifest()
        };

        var http = app.Factory.CreateHttpClient(app.Identity, out var sharedSecret, fileSystemType);
        var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

        fileMetadata.IsEncrypted = false;

        var descriptor = new UploadFileDescriptor
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
            FileMetadata = fileMetadata
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

        var parts = new List<StreamPart>
        {
            new(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
            new(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
        };

        var svc = RestService.For<IRefitAppTransitSender>(http);
        var response = await svc.TransferStream(parts.ToArray());
        keyHeader.AesKey.Wipe();
        return response;
    }
}
