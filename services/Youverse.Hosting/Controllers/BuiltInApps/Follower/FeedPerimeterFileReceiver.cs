using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Controllers.BuiltInApps.Follower
{
    /// <summary />
    // [ApiController]
    // [Route("api/perimeter/apps/feed")]
    // [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.DataSubscriptionCertificateAuthScheme)]
    public class FeedPerimeterFileReceiver : ControllerBase
    {
        // private readonly FollowerPerimeterService _followerPerimeterService;
        // private readonly IPublicKeyService _rsaPublicKeyService;
        // private readonly DotYouContextAccessor _contextAccessor;
        // private readonly FollowerService _followerService;
        // private readonly IDriveService _driveService;
        // private readonly ITransitService _transitService;
        //
        //
        // /// <summary />
        // public FeedPerimeterFileReceiver(IPublicKeyService rsaPublicKeyService, FollowerPerimeterService followerPerimeterService, DotYouContextAccessor contextAccessor,
        //     FollowerService followerService, IDriveService driveService, ITransitService transitService)
        // {
        //     _rsaPublicKeyService = rsaPublicKeyService;
        //     _followerPerimeterService = followerPerimeterService;
        //     _contextAccessor = contextAccessor;
        //     _followerService = followerService;
        //     _driveService = driveService;
        //     _transitService = transitService;
        // }
        //
        //
        // /// <summary>
        // /// Receives a file from an identity I follow
        // /// </summary>
        // [HttpPost("acceptfile")]
        // public async Task<IActionResult> AcceptDataTransfer([FromBody] RsaEncryptedPayload payload)
        // {
        //     var (isValidPublicKey, payloadBytes) = await _rsaPublicKeyService.DecryptPayloadUsingOfflineKey(payload);
        //     if (isValidPublicKey == false)
        //     {
        //         //TODO: extend with error code indicated a bad public key 
        //         return BadRequest("Invalid Public Key");
        //     }
        //
        //     // Validate this caller is someone I follow
        //     var caller = _contextAccessor.GetCurrent().Caller.DotYouId;
        //     var identityIFollow = await _followerService.GetIdentityIFollow(caller);
        //     if (null == identityIFollow)
        //     {
        //         throw new YouverseSecurityException($"Not following {caller}");
        //     }
        //
        //     var transferInstructionSet = DotYouSystemSerializer.Deserialize<RsaEncryptedRecipientTransferInstructionSet>(payloadBytes.ToStringFromUtf8Bytes());
        //
        //     // Now that we have the file, we need to write it to the feed drive
        //
        //     var driveId = await _driveService.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, failIfInvalid: true);
        //     var fileId = _driveService.CreateInternalFileId(driveId.GetValueOrDefault());
        //
        //     var bytes = new byte[100];
        //     var data = new MemoryStream(bytes); //TODO
        //
        //     //This section mimics Transit in that we store the metafile, then queue it to be accepted into the system via the normal ProcessTransitInstructions method
        //     
        //     // Write to temp data so a background process or the feed app can unpack the file
        //     //
        //     await using var stream = new MemoryStream(DotYouSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray());
        //     await _driveService.WriteTempStream(fileId, MultipartHostTransferParts.TransferKeyHeader.ToString().ToLower(), stream);
        //     await _driveService.WriteTempStream(fileId, MultipartUploadParts.Metadata.ToString(), fileMetadata);
        //     
        //     await _transitService.AcceptTransfer(fileId, 0);
        //
        //     return Ok();
        // }
    }
}