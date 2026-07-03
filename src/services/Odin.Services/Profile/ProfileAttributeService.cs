using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;

namespace Odin.Services.Profile;

/// <summary>
/// Server-side authority for <b>writing</b> built-in profile attributes to the
/// <see cref="SystemDriveConstants.ProfileDrive"/>. It is the profile-side analogue of
/// <see cref="ContactService"/>: every app write funnels through here and is performed on the caller's
/// behalf via <see cref="OdinContextUpgrades.UpgradeToByPassAclCheck"/>, gated on
/// <see cref="PermissionKeys.ManageProfile"/>. This lets apps eventually be locked to <b>Read</b> (or no
/// direct grant) on the ProfileDrive — the drive's <see cref="DrivePermission.Write"/> grant becomes
/// removable, with this service the only write path.
///
/// <para>
/// The file shape matches odin-js <c>saveProfileAttribute</c> byte-for-byte so attributes written here
/// are indistinguishable from ones the owner app wrote and render in the profile UI: <c>fileType</c>
/// <see cref="AttributeFileType"/>, <c>uniqueId</c> = the attribute id, <c>tags</c> =
/// <c>[type, sectionId, profileId, id]</c>, <c>groupId</c> = the sectionId, content = the JSON attribute
/// <c>{ id, profileId, type, priority, sectionId, data }</c>. The server owns full construction: it derives
/// the standard-profile <c>profileId</c> and the per-type <c>sectionId</c> (see <see cref="SectionForType"/>),
/// so callers only supply a built-in type and its data.
/// </para>
///
/// <para>
/// <b>Scope (this increment):</b> scalar/text attributes via <see cref="SetAttributeAsync"/> — the same
/// set <see cref="ContactEnrichmentService"/> reads (name, nickname, address, birthday, phone, email,
/// status, link, social/game handles) — plus the Photo attribute via the dedicated
/// <see cref="SetPhotoAttributeAsync"/>, which carries an image payload + generated thumbnails instead of
/// header-only content. Experience/Theme attributes and the general header-overflow payload path remain
/// out of scope; content that does not fit the file header is rejected.
/// </para>
/// </summary>
public class ProfileAttributeService(
    ILogger<ProfileAttributeService> logger,
    StandardFileSystem fileSystem)
{
    /// <summary>File type of a profile attribute on the ProfileDrive (odin-js <c>AttributeConfig.AttributeFileType</c>).</summary>
    public const int AttributeFileType = 77;

    /// <summary>
    /// Payload key for the Photo attribute's image (odin-js <c>PHOTO_PAYLOAD_KEY</c>). Must satisfy
    /// <c>^[a-z0-9_]{8,10}$</c> (see <see cref="TenantPathManager.IsValidPayloadKey"/>).
    /// </summary>
    private const string PhotoPayloadKey = "prfl_key";

    /// <summary>
    /// Max size of the Photo attribute's full-size image. Generous headroom over a realistic profile photo
    /// (a 400x400 rendition typically runs well under 200KB) — this is a sanity ceiling against a buggy or
    /// abusive caller, not a target size. Nothing else in the write path bounds this: unlike thumbnails
    /// (<see cref="ThumbnailDescriptor.MaxThumbnailSize"/>, enforced via <c>ServerFileHeader.Validate</c>),
    /// the main payload has no size check downstream, and Kestrel's request body size limit is unbounded
    /// (<c>Program.cs</c> sets <c>MaxRequestBodySize = null</c>).
    /// </summary>
    private const int MaxPhotoContentBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Max size (UTF-8 bytes) of the attribute JSON carried inline in the file header. Mirrors odin-js
    /// (<c>MAX_HEADER_CONTENT_BYTES</c>), which keeps content under the 10 KB local-app-content server
    /// limit with room for encryption. Larger attributes (rich-text bios, images) ride a payload — the
    /// out-of-scope overflow path — so we reject rather than silently truncate.
    /// </summary>
    private const int MaxHeaderContentBytes = 7000;

    private static readonly TargetDrive Drive = SystemDriveConstants.ProfileDrive;
    private static Guid DriveId => Drive.Alias;

    /// <summary>
    /// The standard profile's id (odin-js <c>BuiltInProfiles.StandardProfileId</c>). It is the ProfileDrive
    /// alias, so a standard-profile attribute file always lands on <see cref="Drive"/>.
    /// </summary>
    private static readonly Guid StandardProfileId = Drive.Alias;

    // Profile section ids — odin-js BuiltInProfiles.*SectionId = toGuidId("<name>") = md5("<name>"). Pinned
    // here so the server never recomputes an md5 at runtime (same convention as BuiltInProfileAttributes).
    private static readonly Guid PersonalInfoSectionId = new("158c7768-8016-2cb8-7f95-dcb3ecb587b0"); // toGuidId("PersonalInfoSection")
    private static readonly Guid ExternalLinksSectionId = new("d37d864d-0dc3-1aa7-dca5-be7cde816406"); // toGuidId("ExternalLinksSection")
    private static readonly Guid AboutSectionId = new("fd2ddd86-16b6-4814-aaef-168775757632");         // toGuidId("AboutSection")

    /// <summary>
    /// Create or edit a built-in profile attribute. When <paramref name="request"/> carries an
    /// <see cref="SetProfileAttributeRequest.Id"/> that already exists, this edits it (optimistic
    /// concurrency on <see cref="SetProfileAttributeRequest.ExpectedVersionTag"/>); otherwise a new
    /// attribute file is created with a server-generated id. The data object is written wholesale (the
    /// caller supplies the full desired field set, matching the odin-js save contract).
    /// </summary>
    public async Task<ProfileAttributeWriteResult> SetAttributeAsync(SetProfileAttributeRequest request,
        IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(request.Type, nameof(request.Type));
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageProfile);

        var attributeType = AssertSupportedType(request.Type);
        var sectionId = SectionForType(attributeType);
        var data = request.Data ?? new Dictionary<string, object>();
        var priority = request.Priority ?? 0;

        var writeContext = GetWriteContext(odinContext);

        // create-or-edit: a supplied id that resolves to an existing file is an edit; anything else creates.
        var attributeId = request.Id ?? Guid.NewGuid();
        var existing = request.Id.HasValue ? await GetForWritingAsync(request.Id.Value, writeContext) : null;

        if (existing == null)
        {
            var createdVersionTag = await WriteNewAsync(attributeId, attributeType, sectionId, priority, data,
                request.Visibility, writeContext);
            return new ProfileAttributeWriteResult
            {
                Outcome = ProfileAttributeWriteOutcome.Created,
                Id = attributeId,
                VersionTag = createdVersionTag
            };
        }

        var currentVersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault();
        if (request.ExpectedVersionTag.GetValueOrDefault() != currentVersionTag)
        {
            logger.LogDebug("Profile attribute update version conflict for id {id} (caller {caller} vs current {current})",
                attributeId, request.ExpectedVersionTag, currentVersionTag);

            return new ProfileAttributeWriteResult
            {
                Outcome = ProfileAttributeWriteOutcome.VersionConflict,
                Id = attributeId,
                VersionTag = currentVersionTag
            };
        }

        var newVersionTag = await OverwriteAsync(existing, attributeId, attributeType, sectionId, priority, data,
            request.Visibility, writeContext);
        return new ProfileAttributeWriteResult
        {
            Outcome = ProfileAttributeWriteOutcome.Updated,
            Id = attributeId,
            VersionTag = newVersionTag
        };
    }

    /// <summary>
    /// Create or edit the Photo attribute (odin-js <c>BuiltInAttributes.Photo</c>). Unlike
    /// <see cref="SetAttributeAsync"/>, the attribute's image rides as a drive-file <b>payload</b> (plus
    /// caller-supplied thumbnails) rather than header content — the header's <c>data.profileImageKey</c>
    /// field just points at <see cref="PhotoPayloadKey"/>, matching odin-js <c>photoAttributeProcessing</c>.
    /// The server does not resize images: the caller supplies the full-size image and every thumbnail
    /// rendition it wants stored, exactly as odin-js generates them client-side before upload.
    ///
    /// <para>
    /// Same create-or-edit-by-<see cref="SetPhotoAttributeRequest.Id"/> contract as
    /// <see cref="SetAttributeAsync"/>, including per-request <see cref="SetPhotoAttributeRequest.Visibility"/>
    /// — callers can hold multiple Photo attributes at once (e.g. a public avatar and a higher-resolution
    /// Connected-only one), each independently addressable and ACL'd.
    /// </para>
    /// </summary>
    public async Task<ProfileAttributeWriteResult> SetPhotoAttributeAsync(SetPhotoAttributeRequest request,
        IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertIsTrue(request.Content is { Length: > 0 }, "Photo content is required");
        if (request.Content.Length > MaxPhotoContentBytes)
        {
            throw new OdinClientException($"Photo content exceeds the {MaxPhotoContentBytes} byte limit",
                OdinClientErrorCode.MaxContentLengthExceeded);
        }
        OdinValidationUtils.AssertIsTrue(!string.IsNullOrWhiteSpace(request.ContentType), "Photo content type is required");
        foreach (var thumbnail in request.Thumbnails ?? [])
        {
            OdinValidationUtils.AssertIsTrue(thumbnail.Content is { Length: > 0 }, "Thumbnail content is required");
            OdinValidationUtils.AssertIsTrue(!string.IsNullOrWhiteSpace(thumbnail.ContentType), "Thumbnail content type is required");
            OdinValidationUtils.AssertIsTrue(thumbnail.PixelWidth > 0 && thumbnail.PixelHeight > 0,
                "Thumbnail dimensions are required");
        }

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageProfile);

        var writeContext = GetWriteContext(odinContext);

        var attributeId = request.Id ?? Guid.NewGuid();
        var existing = request.Id.HasValue ? await GetForWritingAsync(request.Id.Value, writeContext) : null;

        if (existing == null)
        {
            var createdVersionTag = await WriteNewPhotoAsync(attributeId, request, writeContext);
            return new ProfileAttributeWriteResult
            {
                Outcome = ProfileAttributeWriteOutcome.Created,
                Id = attributeId,
                VersionTag = createdVersionTag
            };
        }

        var currentVersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault();
        if (request.ExpectedVersionTag.GetValueOrDefault() != currentVersionTag)
        {
            logger.LogDebug("Photo attribute update version conflict for id {id} (caller {caller} vs current {current})",
                attributeId, request.ExpectedVersionTag, currentVersionTag);

            return new ProfileAttributeWriteResult
            {
                Outcome = ProfileAttributeWriteOutcome.VersionConflict,
                Id = attributeId,
                VersionTag = currentVersionTag
            };
        }

        var newVersionTag = await OverwritePhotoAsync(existing, attributeId, request, writeContext);
        return new ProfileAttributeWriteResult
        {
            Outcome = ProfileAttributeWriteOutcome.Updated,
            Id = attributeId,
            VersionTag = newVersionTag
        };
    }

    private async Task<Guid> WriteNewPhotoAsync(Guid attributeId, SetPhotoAttributeRequest request, IOdinContext writeContext)
    {
        var file = await fileSystem.Storage.CreateInternalFileId(DriveId, writeContext);
        var encrypt = Encrypts(request.Visibility);
        var keyHeader = encrypt ? KeyHeader.NewRandom16() : KeyHeader.Empty();

        var payloadDescriptor = await StagePhotoPayloadAsync(file, request, keyHeader, encrypt, writeContext);
        try
        {
            var metadata = BuildPhotoMetadata(file, attributeId, request.Priority ?? 0, payloadDescriptor, keyHeader,
                encrypt, versionTag: null);
            var serverMetadata = new ServerMetadata
            {
                AccessControlList = AclFor(request.Visibility),
                AllowDistribution = false
            };

            var (success, _) = await fileSystem.Storage.CommitNewFile(file, keyHeader, metadata, serverMetadata,
                ignorePayload: false, writeContext, sourceArea: StagingArea.Upload);
            if (!success)
            {
                throw new OdinSystemException("Failed to commit photo attribute");
            }

            return metadata.VersionTag.GetValueOrDefault();
        }
        finally
        {
            await fileSystem.Storage.CleanupUploadTemporaryFiles(file, [payloadDescriptor], writeContext);
        }
    }

    private async Task<Guid> OverwritePhotoAsync(ServerFileHeader existing, Guid attributeId,
        SetPhotoAttributeRequest request, IOdinContext writeContext)
    {
        var file = existing.FileMetadata.File;
        var encrypt = Encrypts(request.Visibility);
        var keyHeader = encrypt ? KeyHeader.NewRandom16() : KeyHeader.Empty();

        // Same full-rebuild convention as OverwriteAsync: OverwriteFile treats newMetadata.Payloads as the
        // complete desired payload set and hard-deletes whatever isn't referenced anymore, so the prior
        // image (and its thumbnails) are cleaned up automatically once the new one commits.
        var payloadDescriptor = await StagePhotoPayloadAsync(file, request, keyHeader, encrypt, writeContext);
        try
        {
            var metadata = BuildPhotoMetadata(file, attributeId, request.Priority ?? 0, payloadDescriptor, keyHeader,
                encrypt, versionTag: existing.FileMetadata.VersionTag.GetValueOrDefault());
            var serverMetadata = new ServerMetadata
            {
                AccessControlList = AclFor(request.Visibility),
                AllowDistribution = false
            };

            var (success, _) = await fileSystem.Storage.OverwriteFile(file, file, keyHeader, metadata, serverMetadata,
                ignorePayload: false, writeContext, markComplete: null, sourceArea: StagingArea.Upload);
            if (!success)
            {
                throw new OdinSystemException("Failed to commit photo attribute");
            }

            return metadata.VersionTag.GetValueOrDefault();
        }
        finally
        {
            await fileSystem.Storage.CleanupUploadTemporaryFiles(file, [payloadDescriptor], writeContext);
        }
    }

    /// <summary>
    /// Stages the image + thumbnails to the upload temp area and returns the payload descriptor. Encrypted
    /// attributes share one payload IV across the image and every thumbnail (matching
    /// <see cref="ContactService"/>'s <c>SetContactImageRequest</c> convention — thumbnails have no IV of
    /// their own).
    /// </summary>
    private async Task<PayloadDescriptor> StagePhotoPayloadAsync(InternalDriveFileId file, SetPhotoAttributeRequest request,
        KeyHeader keyHeader, bool encrypt, IOdinContext writeContext)
    {
        var uid = UnixTimeUtcUnique.Now();
        var iv = encrypt ? ByteArrayUtil.GetRndByteArray(16) : new byte[16];

        byte[] EncryptIfNeeded(byte[] plain) =>
            encrypt ? new KeyHeader { Iv = iv, AesKey = keyHeader.AesKey }.EncryptDataAes(plain) : plain;

        var imageExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(PhotoPayloadKey, uid);
        var imageBytesWritten = await fileSystem.Storage.WriteUploadStream(file, imageExtension,
            new MemoryStream(EncryptIfNeeded(request.Content)), writeContext);

        var thumbnailDescriptors = new List<ThumbnailDescriptor>();
        foreach (var thumbnail in request.Thumbnails ?? [])
        {
            var thumbExtension = TenantPathManager.GetThumbnailFileNameAndExtension(
                PhotoPayloadKey, uid, thumbnail.PixelWidth, thumbnail.PixelHeight);
            var thumbBytesWritten = await fileSystem.Storage.WriteUploadStream(file, thumbExtension,
                new MemoryStream(EncryptIfNeeded(thumbnail.Content)), writeContext);

            thumbnailDescriptors.Add(new ThumbnailDescriptor
            {
                PixelWidth = thumbnail.PixelWidth,
                PixelHeight = thumbnail.PixelHeight,
                ContentType = thumbnail.ContentType,
                BytesWritten = thumbBytesWritten
            });
        }

        return new PayloadDescriptor
        {
            Key = PhotoPayloadKey,
            Uid = uid,
            Iv = iv,
            ContentType = request.ContentType,
            BytesWritten = imageBytesWritten,
            LastModified = UnixTimeUtc.Now(),
            Thumbnails = thumbnailDescriptors
        };
    }

    /// <summary>
    /// Builds the Photo attribute's <see cref="FileMetadata"/>: header content is just the pointer
    /// <c>{ data: { profileImageKey: PhotoPayloadKey } }</c> (odin-js overwrites the field the same way
    /// after staging the image), so it never risks tripping <see cref="AssertContentFitsHeader"/> the way
    /// an inline image would. Encrypted the same way <see cref="BuildHeaderAsync"/> encrypts text-attribute
    /// content — under <paramref name="keyHeader"/> when <paramref name="encrypt"/> is set — so
    /// <c>IsEncrypted</c> and the actual stored bytes never disagree the way the image payload already
    /// doesn't (see <see cref="StagePhotoPayloadAsync"/>).
    /// </summary>
    private FileMetadata BuildPhotoMetadata(InternalDriveFileId file, Guid attributeId, int priority,
        PayloadDescriptor payloadDescriptor, KeyHeader keyHeader, bool encrypt, Guid? versionTag)
    {
        var data = new Dictionary<string, object> { [ProfileAttributeFields.ProfileImageKey] = PhotoPayloadKey };
        var content = new ProfileAttributeContent
        {
            Id = attributeId.ToString("N"),
            ProfileId = StandardProfileId.ToString("N"),
            Type = BuiltInProfileAttributes.Photo.ToString("N"),
            Priority = priority,
            SectionId = PersonalInfoSectionId.ToString("N"),
            Data = data
        };

        var json = OdinSystemSerializer.Serialize(content);
        var storedContent = encrypt ? keyHeader.EncryptDataAes(json.ToUtf8ByteArray()).ToBase64() : json;
        AssertContentFitsHeader(storedContent);

        return new FileMetadata(file)
        {
            AppData = new AppFileMetaData
            {
                FileType = AttributeFileType,
                UniqueId = attributeId,
                Tags = [BuiltInProfileAttributes.Photo, PersonalInfoSectionId, StandardProfileId, attributeId],
                GroupId = PersonalInfoSectionId,
                Content = storedContent
            },
            IsEncrypted = encrypt,
            VersionTag = versionTag,
            FileState = FileState.Active,
            Payloads = [payloadDescriptor]
        };
    }

    /// <summary>
    /// Delete the attribute addressed by <paramref name="attributeId"/> (soft delete). Optimistic
    /// concurrency on <paramref name="expectedVersionTag"/>. Returns false when no such attribute exists.
    /// </summary>
    public async Task<bool> DeleteAttributeAsync(Guid attributeId, Guid expectedVersionTag, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotEmptyGuid(attributeId, nameof(attributeId));
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageProfile);

        var writeContext = GetWriteContext(odinContext);

        var existing = await GetForWritingAsync(attributeId, writeContext);
        if (existing == null)
        {
            return false;
        }

        var currentVersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault();
        if (expectedVersionTag != currentVersionTag)
        {
            throw new OdinClientException(
                $"Profile attribute {attributeId} version conflict (caller {expectedVersionTag} vs current {currentVersionTag})",
                OdinClientErrorCode.VersionTagMismatch);
        }

        await fileSystem.Storage.SoftDeleteLongTermFile(existing.FileMetadata.File, writeContext, null);
        return true;
    }

    // Grant ONLY Write here, not ReadWrite: the caller's real Read grant on the ProfileDrive supplies the
    // storage key (needed to encrypt the key header of non-public attributes) and read access, so the
    // bypass just adds the missing Write permission. Least-privilege, and correct once apps are locked to
    // Read on the drive (the funnel that lets direct Write be removed).
    private static IOdinContext GetWriteContext(IOdinContext odinContext)
    {
        return OdinContextUpgrades.UpgradeToByPassAclCheck(Drive, DrivePermission.Write, odinContext);
    }

    private async Task<ServerFileHeader> GetForWritingAsync(Guid attributeId, IOdinContext writeContext)
    {
        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = false,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = false
        };

        var match = await fileSystem.Query.GetFileByClientUniqueId(DriveId, attributeId, options, writeContext);
        if (match == null)
        {
            return null;
        }

        var file = new InternalDriveFileId(DriveId, match.FileId);
        return await fileSystem.Storage.GetServerFileHeaderForWriting(file, writeContext);
    }

    private async Task<Guid> WriteNewAsync(Guid attributeId, Guid attributeType, Guid sectionId, int priority,
        Dictionary<string, object> data, ProfileAttributeVisibility visibility, IOdinContext writeContext)
    {
        var file = await fileSystem.Storage.CreateInternalFileId(DriveId, writeContext);

        var (header, _) = await BuildHeaderAsync(file, attributeId, attributeType, sectionId, priority, data,
            visibility, SequentialGuid.CreateGuid(), writeContext);
        await fileSystem.Storage.WriteNewFileHeader(file, header, writeContext, raiseEvent: true);

        return header.FileMetadata.VersionTag.GetValueOrDefault();
    }

    private async Task<Guid> OverwriteAsync(ServerFileHeader existing, Guid attributeId, Guid attributeType,
        Guid sectionId, int priority, Dictionary<string, object> data, ProfileAttributeVisibility visibility,
        IOdinContext writeContext)
    {
        var file = existing.FileMetadata.File;

        // Rebuild the whole header from the target visibility. CreateServerFileHeader sets the encrypted key
        // header for us (a real key for encrypted attributes, EncryptedKeyHeader.Empty for public ones), so
        // this transparently handles an attribute that flips between encrypted and plaintext on edit — the
        // case odin-js handles with a full re-upload. The header carries the file's current version tag (the
        // optimistic-concurrency expectation); the write advances it and writes the new tag back onto the header.
        var (header, _) = await BuildHeaderAsync(file, attributeId, attributeType, sectionId, priority, data,
            visibility, existing.FileMetadata.VersionTag.GetValueOrDefault(), writeContext);
        await fileSystem.Storage.UpdateActiveFileHeader(file, header, writeContext, raiseEvent: true);

        return header.FileMetadata.VersionTag.GetValueOrDefault();
    }

    /// <summary>
    /// Builds the <see cref="ServerFileHeader"/> for an attribute file from the target visibility: encrypts
    /// the content (and key header) for Connected/Owner attributes, leaves Anonymous/Authenticated ones
    /// plaintext (matching odin-js <c>encrypt = !(Anonymous || Authenticated)</c>). Asserts the content fits
    /// the file header.
    /// </summary>
    private async Task<(ServerFileHeader header, bool encrypt)> BuildHeaderAsync(InternalDriveFileId file,
        Guid attributeId, Guid attributeType, Guid sectionId, int priority, Dictionary<string, object> data,
        ProfileAttributeVisibility visibility, Guid versionTag, IOdinContext writeContext)
    {
        ApplyDerivedFields(attributeType, data);

        var content = new ProfileAttributeContent
        {
            Id = attributeId.ToString("N"),
            ProfileId = StandardProfileId.ToString("N"),
            Type = attributeType.ToString("N"),
            Priority = priority,
            SectionId = sectionId.ToString("N"),
            Data = data
        };

        var json = OdinSystemSerializer.Serialize(content);
        var encrypt = Encrypts(visibility);

        KeyHeader keyHeader;
        string storedContent;
        if (encrypt)
        {
            keyHeader = KeyHeader.NewRandom16();
            storedContent = keyHeader.EncryptDataAes(json.ToUtf8ByteArray()).ToBase64();
        }
        else
        {
            keyHeader = KeyHeader.Empty();
            storedContent = json;
        }

        AssertContentFitsHeader(storedContent);

        var metadata = new FileMetadata(file)
        {
            AppData = new AppFileMetaData
            {
                FileType = AttributeFileType,
                UniqueId = attributeId,
                // odin-js tag order: [type, sectionId, profileId, id]. Type + sectionId are what the client
                // queries on (tagsMatchAtLeastOne / groupId); profileId + id round out the set.
                Tags = [attributeType, sectionId, StandardProfileId, attributeId],
                GroupId = sectionId,
                Content = storedContent
            },
            IsEncrypted = encrypt,
            VersionTag = versionTag,
            // UpdateActiveFileHeader asserts the incoming header is Active before adopting the stored file's
            // state, so set it explicitly (the FileMetadata default is Deleted).
            FileState = FileState.Active,
            Payloads = []
        };

        var serverMetadata = new ServerMetadata
        {
            AccessControlList = AclFor(visibility),
            AllowDistribution = false
        };

        var header = await fileSystem.Storage.CreateServerFileHeader(file, keyHeader, metadata, serverMetadata,
            writeContext);
        return (header, encrypt);
    }

    /// <summary>
    /// Applies the per-type derived fields odin-js computes before saving. For the Name attribute that is
    /// <c>data.displayName</c> (from an explicit display name, else given + surname), which downstream
    /// readers — including <see cref="ContactEnrichmentService"/> — rely on.
    /// </summary>
    private static void ApplyDerivedFields(Guid attributeType, Dictionary<string, object> data)
    {
        if (attributeType != BuiltInProfileAttributes.Name)
        {
            return;
        }

        var explicitName = Str(data, ProfileAttributeFields.ExplicitDisplayName)?.Trim();
        var displayName = !string.IsNullOrEmpty(explicitName)
            ? explicitName
            : string.Join(" ", new[] { Str(data, ProfileAttributeFields.GivenName), Str(data, ProfileAttributeFields.Surname) }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            data[ProfileAttributeFields.DisplayName] = displayName;
        }
    }

    private void AssertContentFitsHeader(string storedContent)
    {
        if (storedContent.ToUtf8ByteArray().Length > MaxHeaderContentBytes)
        {
            throw new OdinClientException(
                "Profile attribute content is too large to store in the file header. Attributes with payloads " +
                "(images, rich-text bios) are not supported by this API.",
                OdinClientErrorCode.MaxContentLengthExceeded);
        }
    }

    /// <summary>Asserts the type is a known built-in attribute (and not Photo, which has its own payload-carrying
    /// write path) and returns its canonical GUID.</summary>
    private static Guid AssertSupportedType(Guid attributeType)
    {
        if (attributeType == BuiltInProfileAttributes.Photo)
        {
            throw new OdinClientException(
                $"The Photo attribute carries an image payload and is not supported by {nameof(SetAttributeAsync)}; use {nameof(SetPhotoAttributeAsync)}.",
                OdinClientErrorCode.ArgumentError);
        }

        var match = BuiltInProfileAttributes.All.FirstOrDefault(t => t.Type == attributeType);
        if (match == null)
        {
            throw new OdinClientException($"Unknown profile attribute type {attributeType:N}",
                OdinClientErrorCode.ArgumentError);
        }

        return match.Type;
    }

    /// <summary>
    /// Maps a built-in attribute type to the standard-profile section it belongs to (odin-js
    /// <c>SetupProvider</c>), keyed off the attribute's <see cref="ProfileAttributeCategory"/> so it cannot
    /// drift from the registry. Financial (credit-card) attributes live in the separate Wallet profile and
    /// are out of scope.
    /// </summary>
    private static Guid SectionForType(Guid attributeType)
    {
        var category = BuiltInProfileAttributes.All.First(t => t.Type == attributeType).Category;
        return category switch
        {
            ProfileAttributeCategory.Personal => PersonalInfoSectionId,
            ProfileAttributeCategory.Social => ExternalLinksSectionId,
            ProfileAttributeCategory.Game => ExternalLinksSectionId,
            ProfileAttributeCategory.Link => ExternalLinksSectionId,
            ProfileAttributeCategory.Bio => AboutSectionId,
            _ => throw new OdinClientException(
                $"Profile attribute type {attributeType:N} ({category}) is not supported by this API",
                OdinClientErrorCode.ArgumentError)
        };
    }

    /// <summary>Public (Anonymous/Authenticated) attributes are stored plaintext; everything else encrypts.</summary>
    private static bool Encrypts(ProfileAttributeVisibility visibility)
    {
        return visibility is not (ProfileAttributeVisibility.Anonymous or ProfileAttributeVisibility.Authenticated);
    }

    private static AccessControlList AclFor(ProfileAttributeVisibility visibility)
    {
        return visibility switch
        {
            ProfileAttributeVisibility.Anonymous => AccessControlList.Anonymous,
            ProfileAttributeVisibility.Authenticated => AccessControlList.Authenticated,
            ProfileAttributeVisibility.Connected => AccessControlList.Connected,
            ProfileAttributeVisibility.Owner => AccessControlList.OwnerOnly,
            _ => throw new OdinClientException($"Unsupported visibility {visibility}", OdinClientErrorCode.ArgumentError)
        };
    }

    private static string Str(Dictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        var s = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
