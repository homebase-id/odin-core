using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Swashbuckle.AspNetCore.Annotations;
using Odin.Services.Apps.Builtin;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Apps;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveManagementV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerDriveManagementController(
        DriveManager driveManager,
        Defragmenter defragmenter,
        IAppRegistrationService appRegistrationService
        ) : OdinControllerBase
    {
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrives([FromBody] GetDrivesRequest request)
        {
            var drives = await driveManager.GetDrivesAsync(new PageOptions(request.PageNumber, request.PageSize), WebOdinContext);

            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    DriveId = drive.Id,
                    Name = drive.Name,
                    TargetDriveInfo = drive.TargetDriveInfo,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads,
                    AllowSubscriptions = drive.AllowSubscriptions,
                    AllowCdn = drive.IsCdnEnabled(),
                    OwnerOnly = drive.OwnerOnly,
                    Attributes = drive.Attributes,
                    IsArchived = drive.IsArchived,
                    IsSystemDrive = BuiltinDrives.IsProtected(drive.Id),
                    AppId = drive.AppId,
                    DriveSlug = drive.DriveSlug,
                    DriveTypeSlug = drive.DriveTypeSlug,
                    WriteOnlyPublicKeyJwk = drive.WriteOnlyKeyPair?.PublicKeyJwk(),
                    WriteOnlyPublicKeyCrc32 = drive.WriteOnlyKeyPair?.crc32c

                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("create")]
        public async Task<bool> CreateDrive([FromBody] CreateDriveRequest request)
        {
            //create a drive on the drive service
            
            var _ = await driveManager.CreateDriveAsync(request, WebOdinContext);
            return true;
        }

        [HttpPost("updatemetadata")]
        public async Task<bool> UpdateDriveMetadata([FromBody] UpdateDriveDefinitionRequest request)
        {
            await driveManager.UpdateMetadataAsync(request.TargetDrive.Alias, request.Metadata, WebOdinContext);
            return true;
        }

        [HttpPost("UpdateAttributes")]
        public async Task<bool> UpdateDriveAttributes([FromBody] UpdateDriveDefinitionRequest request)
        {
            await driveManager.UpdateAttributesAsync(request.TargetDrive.Alias, request.Attributes, WebOdinContext);
            return true;
        }

        [HttpPost("setdrivereadmode")]
        public async Task<IActionResult> SetDriveReadMode([FromBody] UpdateDriveReadModeRequest request)
        {
            await driveManager.SetDriveReadModeAsync(request.TargetDrive.Alias, request.AllowAnonymousReads, WebOdinContext);
            return Ok();
        }
        
        [HttpPost("set-allow-subscriptions")]
        public async Task<IActionResult> SetDriveAllowSubscriptions([FromBody] UpdateDriveAllowSubscriptionsRequest request)
        {
            await driveManager.SetDriveAllowSubscriptionsAsync(request.TargetDrive.Alias, request.AllowSubscriptions, WebOdinContext);
            return Ok();
        }

        [HttpPost("set-allow-cdn")]
        public async Task<IActionResult> SetDriveAllowCdn([FromBody] UpdateDriveAllowCdnRequest request)
        {
            await driveManager.SetDriveAllowCdnAsync(request.TargetDrive.Alias, request.AllowCdn, WebOdinContext);
            return Ok();
        }

        [HttpPost("set-archive-drive")]
        public async Task<IActionResult> SetArchiveDriveFlag([FromBody] UpdateDriveArchiveFlag request)
        {
            await driveManager.SetArchiveDriveFlagAsync(request.TargetDrive.Alias, request.Archived, WebOdinContext);
            return Ok();
        }

        /// <summary>
        /// Hands a drive that belongs to no app to one that does exist.
        /// </summary>
        /// <remarks>
        /// Owner console only -- this whole controller is.  An app must not be able to hand itself a
        /// drive, which is the same reason circle adoption is owner-only.
        /// <para>
        /// The app is looked up here rather than in <c>DriveManager</c>, and not because the layer is
        /// nicer: <c>IAppRegistrationService</c> depends on <c>ExchangeGrantService</c>, which depends
        /// on <c>IDriveManager</c>, so resolving it down there is a cycle.
        /// </para>
        /// <para>
        /// It is checked at all, unlike in <c>CreateDriveAsync</c> where AppId is taken on trust.  That
        /// exemption exists because provisioning creates drives before it registers apps and validating
        /// would invert the dependency -- an ordering that cannot arise here, where the caller is a
        /// person at a console adopting a drive that already exists.  Left unchecked, a mistyped id
        /// would strand the drive: owned by nothing real, and no longer adoptable.
        /// </para>
        /// </remarks>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("set-owner")]
        public async Task<IActionResult> SetDriveOwningApp([FromBody] SetDriveOwningAppRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
            OdinValidationUtils.AssertNotEmptyGuid(request.AppId, nameof(request.AppId));

            var app = await appRegistrationService.GetAppRegistration(request.AppId, WebOdinContext);
            if (app == null)
            {
                throw new OdinClientException($"No app is registered with id {request.AppId}",
                    OdinClientErrorCode.AppNotRegistered);
            }

            await driveManager.SetDriveOwningAppAsync(request.TargetDrive.Alias, request.AppId,
                request.DriveSlug, request.DriveTypeSlug, WebOdinContext);

            return Ok();
        }


        /// <summary>
        /// Moves a drive from the app that owns it to another, at a new address.
        /// </summary>
        /// <remarks>
        /// The escape hatch out of <see cref="SetDriveOwningApp"/>'s one-way rule.  Master key
        /// required, so the owner console and nothing else -- an app that could move a drive to
        /// itself could help itself to the drive's address.
        /// <para>
        /// The old address stops resolving.  A slug is required rather than derived for exactly that
        /// reason: the caller states the new address instead of discovering it afterwards.
        /// </para>
        /// </remarks>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("reassign-owner")]
        public async Task<IActionResult> ReassignDriveOwningApp([FromBody] SetDriveOwningAppRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
            OdinValidationUtils.AssertNotEmptyGuid(request.AppId, nameof(request.AppId));

            var app = await appRegistrationService.GetAppRegistration(request.AppId, WebOdinContext);
            if (app == null)
            {
                throw new OdinClientException($"No app is registered with id {request.AppId}",
                    OdinClientErrorCode.AppNotRegistered);
            }

            await driveManager.ReassignDriveOwningAppAsync(request.TargetDrive.Alias, request.AppId,
                request.DriveSlug, request.DriveTypeSlug, WebOdinContext);

            return Ok();
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpGet("type")]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrivesByType([FromQuery] GetDrivesByTypeRequest request)
        {
            
            var drives = await driveManager.GetDrivesAsync(request.DriveType, new PageOptions(request.PageNumber, request.PageSize), WebOdinContext);
            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    Name = drive.Name,
                    TargetDriveInfo = drive.TargetDriveInfo,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads,
                    AllowSubscriptions = drive.AllowSubscriptions,
                    AllowCdn = drive.IsCdnEnabled(),
                    OwnerOnly = drive.OwnerOnly,
                    Attributes = drive.Attributes,
                    IsArchived = drive.IsArchived,
                    IsSystemDrive = BuiltinDrives.IsProtected(drive.Id),
                    AppId = drive.AppId,
                    DriveSlug = drive.DriveSlug,
                    DriveTypeSlug = drive.DriveTypeSlug,
                    WriteOnlyPublicKeyJwk = drive.WriteOnlyKeyPair?.PublicKeyJwk(),
                    WriteOnlyPublicKeyCrc32 = drive.WriteOnlyKeyPair?.crc32c
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }

        [HttpPost("defrag")]
        public async Task<IActionResult> DefragDrive()
        {
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            await defragmenter.Defragment();
            return Ok();
        }
    }

    public class UpdateDriveDefinitionRequest
    {
        public TargetDrive TargetDrive { get; set; }

        public string Metadata { get; set; }

        public Dictionary<string, string> Attributes { get; set; }
    }

    public class UpdateDriveReadModeRequest
    {
        public TargetDrive TargetDrive { get; set; }
        public bool AllowAnonymousReads { get; set; }
    }
    
    public class UpdateDriveAllowSubscriptionsRequest
    {
        public TargetDrive TargetDrive { get; set; }
        public bool AllowSubscriptions { get; set; }
    }
    
    public class UpdateDriveAllowCdnRequest
    {
        public TargetDrive TargetDrive { get; set; }
        public bool AllowCdn { get; set; }
    }

    public class UpdateDriveArchiveFlag
    {
        public TargetDrive TargetDrive { get; set; }
        public bool Archived { get; set; }
    }

    public class SetDriveOwningAppRequest
    {
        /// <summary>The drive to adopt.  Must currently belong to no app, and must not be provisioned.</summary>
        public TargetDrive TargetDrive { get; set; }

        /// <summary>The app to hand it to.  Must name a registered app.</summary>
        public Guid AppId { get; set; }

        /// <summary>
        /// The address the drive answers to under that app (<c>/apps/{appSlug}/drives/{driveSlug}</c>).
        /// Optional -- derived from the drive's name when omitted.  Supplied values are never coerced:
        /// a malformed one is rejected and one already taken by this app is refused, not suffixed.
        /// </summary>
        public string DriveSlug { get; set; }

        /// <summary>
        /// Readable form of the drive's type, e.g. <c>channel</c>.  A category to filter on, not an
        /// address.  Optional -- derived from the drive's type when omitted.
        /// </summary>
        public string DriveTypeSlug { get; set; }
    }
}