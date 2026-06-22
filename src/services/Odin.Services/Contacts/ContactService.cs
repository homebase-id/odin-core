using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;

namespace Odin.Services.Contacts;

/// <summary>
/// Server-side authority for <b>writing</b> contacts to the
/// <see cref="SystemDriveConstants.ContactDrive"/>. Reads stay client-side: clients read contacts as
/// plain files from the contact drive (QueryBatch on <c>fileType=100</c>), so there is no read/list API
/// here.
///
/// <para>
/// All writes assert <see cref="PermissionKeys.ManageContacts"/> and are performed on the caller's
/// behalf via <see cref="OdinContextUpgrades.UpgradeToByPassAclCheck"/> (precedent: the Shamir
/// services) — the contact drive grant to apps is read-only, and every app write funnels through here.
/// </para>
/// </summary>
public class ContactService(
    ILogger<ContactService> logger,
    StandardFileSystem fileSystem)
{
    public const int ContactFileType = 100;

    /// <summary>Payload key for the contact's profile image (matches odin-js). Must satisfy <c>^[a-z0-9_]{8,10}$</c>.</summary>
    public const string ProfileImagePayloadKey = "prfl_pic";

    private static readonly TargetDrive Drive = SystemDriveConstants.ContactDrive;
    private static Guid DriveId => Drive.Alias;

    private static readonly JsonSerializerOptions ContentSerializerOptions = new()
    {
        // Property names are pinned on the DTOs; keep the serializer from re-casing or emitting nulls.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Deterministic key for a contact that carries an odinId. Byte-compatible with odin-js
    /// <c>toGuidId(odinId) = md5(odinId)</c>, so introduced → pending → connected all resolve to the
    /// same file.
    /// </summary>
    public static Guid ToContactUniqueId(OdinId odinId)
    {
        return ContactGuid.ToGuidId(odinId.DomainName);
    }

    // Grant ONLY Write here, not ReadWrite: the caller's real Read grant on the ContactDrive supplies
    // the storage key (and read access), so the bypass just needs to add the missing Write permission.
    // This keeps it least-privilege and correct once apps are locked to Read on the drive (phase 2).
    private static IOdinContext GetWriteContext(IOdinContext odinContext)
    {
        return OdinContextUpgrades.UpgradeToByPassAclCheck(Drive, DrivePermission.Write, odinContext);
    }

    /// <summary>
    /// Create a contact. The unique id is deterministic from <c>content.OdinId</c>
    /// (<see cref="ToContactUniqueId"/>) when present, else random. If a contact with that id already
    /// exists (an odinId collision), returns <see cref="ContactWriteOutcome.AlreadyExists"/> carrying
    /// the current header — the caller should <see cref="UpdateAsync"/> instead. Associating an odinId
    /// with an existing unlinked contact (re-keying) is a separate, deferred concern.
    /// </summary>
    public async Task<ContactWriteResult> CreateAsync(ContactContent content, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(content, nameof(content));
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        Guid uniqueId;
        bool deterministic;
        if (!string.IsNullOrWhiteSpace(content.OdinId))
        {
            // Any syntactically valid domain is accepted; no liveness check.
            OdinValidationUtils.AssertIsValidOdinId(content.OdinId, out var parsed);
            content.OdinId = parsed.DomainName; // normalize
            uniqueId = ToContactUniqueId(parsed);
            deterministic = true;
        }
        else
        {
            uniqueId = Guid.NewGuid();
            deterministic = false;
        }

        var writeContext = GetWriteContext(odinContext);

        // A random id cannot collide; only the deterministic (odinId) key needs an existence check.
        var existing = deterministic ? await GetForWritingAsync(uniqueId, writeContext) : null;
        if (existing != null)
        {
            return new ContactWriteResult
            {
                Outcome = ContactWriteOutcome.AlreadyExists,
                UniqueId = uniqueId,
                VersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault(),
                CurrentOnConflict = DriveFileUtility.CreateClientFileHeader(existing, odinContext)
            };
        }

        var (createdUniqueId, createdVersionTag) = await WriteNewAsync(uniqueId, content, writeContext);
        return new ContactWriteResult
        {
            Outcome = ContactWriteOutcome.Created,
            UniqueId = createdUniqueId,
            VersionTag = createdVersionTag
        };
    }

    /// <summary>
    /// Update the contact addressed by <paramref name="uniqueId"/>, merging new content over existing
    /// (does not clobber fields the caller left null/empty). Optimistic concurrency: a stale
    /// <paramref name="versionTag"/> yields <see cref="ContactWriteOutcome.VersionConflict"/> carrying
    /// the current header; a missing contact yields <see cref="ContactWriteOutcome.NotFound"/>. The
    /// route's <paramref name="uniqueId"/> identifies the file — this never re-keys (associating an
    /// odinId with an unlinked contact is deferred). Overwritten non-empty values are appended to the
    /// contact's <c>merge_log</c> payload.
    /// </summary>
    public async Task<ContactWriteResult> UpdateAsync(Guid uniqueId, ContactContent content, Guid versionTag,
        IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(content, nameof(content));
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        if (!string.IsNullOrWhiteSpace(content.OdinId))
        {
            OdinValidationUtils.AssertIsValidOdinId(content.OdinId, out var parsed);
            content.OdinId = parsed.DomainName; // normalize
        }

        var writeContext = GetWriteContext(odinContext);

        var existing = await GetForWritingAsync(uniqueId, writeContext);
        if (existing == null)
        {
            return new ContactWriteResult { Outcome = ContactWriteOutcome.NotFound, UniqueId = uniqueId };
        }

        var currentVersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault();
        if (versionTag != currentVersionTag)
        {
            logger.LogDebug("Contact update version conflict for uid {uid} (caller {caller} vs current {current})",
                uniqueId, versionTag, currentVersionTag);

            return VersionConflictResult(uniqueId, existing, currentVersionTag, odinContext);
        }

        // Owner edits never touch ext_data (peer-only); pass null so any stored ext_data is preserved.
        var newVersionTag = await OverwriteAsync(existing, content, ContactMergeSource.Api, null, writeContext);
        return new ContactWriteResult
        {
            Outcome = ContactWriteOutcome.Updated,
            UniqueId = uniqueId,
            VersionTag = newVersionTag
        };
    }

    /// <summary>
    /// Ensure a data-only contact file exists for <paramref name="odinId"/> (creates a <c>{ odinId }</c>
    /// stub if absent; no-op if present). Used by the lifecycle handlers to materialize a contact the
    /// moment a relationship appears, before any profile data is available. Fast + local (no peer I/O).
    /// </summary>
    public async Task EnsureExistsAsync(OdinId odinId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        var uniqueId = ToContactUniqueId(odinId);
        var writeContext = GetWriteContext(odinContext);

        var existing = await GetForWritingAsync(uniqueId, writeContext);
        if (existing != null)
        {
            return;
        }

        await WriteNewAsync(uniqueId, new ContactContent { OdinId = odinId.DomainName }, writeContext);
    }

    /// <summary>
    /// Soft-delete the contact keyed on <paramref name="uniqueId"/>. Returns false if no such contact
    /// exists.
    /// </summary>
    /// <remarks>
    /// This deletes the contact <i>data</i> file only. Tearing down a live relationship (disconnect /
    /// cancel / reject) is the connection lifecycle's job and is handled by the caller before invoking
    /// this for a connected contact (see the plan's delete-cascade); a bare soft-delete here would
    /// otherwise be re-created by reconcile.
    /// </remarks>
    public async Task<bool> DeleteByUniqueIdAsync(Guid uniqueId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        var writeContext = GetWriteContext(odinContext);
        var existing = await GetForWritingAsync(uniqueId, writeContext);
        if (existing == null)
        {
            return false;
        }

        await fileSystem.Storage.SoftDeleteLongTermFile(existing.FileMetadata.File, writeContext, null);
        return true;
    }

    /// <summary>
    /// Soft-delete the contact keyed on <paramref name="odinId"/>.
    /// </summary>
    public Task<bool> DeleteByOdinIdAsync(OdinId odinId, IOdinContext odinContext)
    {
        return DeleteByUniqueIdAsync(ToContactUniqueId(odinId), odinContext);
    }

    /// <summary>
    /// Set (create or replace) the contact's profile image. The client sends the image + thumbnail bytes
    /// <b>already encrypted</b> under the contact file's AES key with the request's 16-byte IV (one IV
    /// shared by the image and its thumbnails) over the shared-secret transport; the server stores that
    /// ciphertext <b>verbatim</b> as the <see cref="ProfileImagePayloadKey"/> payload and records the IV
    /// for later decryption. Version-tag gated (stale → conflict, missing → not-found); the contact's
    /// content (re-encrypted under a fresh content IV) and any <c>merge_log</c> payload are preserved.
    /// </summary>
    public async Task<ContactWriteResult> SetImageAsync(Guid uniqueId, SetContactImageRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));
        OdinValidationUtils.AssertIsTrue(request.Content is { Length: > 0 }, "image content is required");
        OdinValidationUtils.AssertIsTrue(request.Iv is { Length: 16 }, "a 16-byte image iv is required");
        OdinValidationUtils.AssertIsTrue(!string.IsNullOrWhiteSpace(request.ContentType), "image contentType is required");
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        var writeContext = GetWriteContext(odinContext);
        var existing = await GetForWritingAsync(uniqueId, writeContext);
        if (existing == null)
        {
            return new ContactWriteResult { Outcome = ContactWriteOutcome.NotFound, UniqueId = uniqueId };
        }

        var currentVersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault();
        if (request.VersionTag != currentVersionTag)
        {
            return VersionConflictResult(uniqueId, existing, currentVersionTag, odinContext);
        }

        var newVersionTag = await WriteImagePayloadAsync(existing, request, writeContext);
        return new ContactWriteResult { Outcome = ContactWriteOutcome.Updated, UniqueId = uniqueId, VersionTag = newVersionTag };
    }

    /// <summary>
    /// Remove the contact's profile image payload. Version-tag gated; returns not-found if the contact
    /// or its image is absent. Content and any <c>merge_log</c> payload are preserved.
    /// </summary>
    public async Task<ContactWriteResult> DeleteImageAsync(Guid uniqueId, Guid versionTag, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        var writeContext = GetWriteContext(odinContext);
        var existing = await GetForWritingAsync(uniqueId, writeContext);
        if (existing == null)
        {
            return new ContactWriteResult { Outcome = ContactWriteOutcome.NotFound, UniqueId = uniqueId };
        }

        var currentVersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault();
        if (versionTag != currentVersionTag)
        {
            return VersionConflictResult(uniqueId, existing, currentVersionTag, odinContext);
        }

        var hasImage = (existing.FileMetadata.Payloads ?? []).Any(p => p.KeyEquals(ProfileImagePayloadKey));
        if (!hasImage)
        {
            return new ContactWriteResult { Outcome = ContactWriteOutcome.NotFound, UniqueId = uniqueId };
        }

        var newVersionTag = await RemoveImagePayloadAsync(existing, writeContext);
        return new ContactWriteResult { Outcome = ContactWriteOutcome.Updated, UniqueId = uniqueId, VersionTag = newVersionTag };
    }

    /// <summary>
    /// Server-internal create-or-merge for a contact that carries an odinId (used by enrichment and the
    /// lifecycle handlers). Unlike the client <see cref="CreateAsync"/> / <see cref="UpdateAsync"/>, this does <b>not</b> do
    /// client-versionTag optimistic concurrency — it always merges over the current file, retrying once
    /// if a concurrent write advances the version mid-merge (the merge is idempotent; reconcile
    /// converges any straggler). Overwritten values are logged to <c>merge_log</c> tagged with
    /// <paramref name="source"/>.
    /// </summary>
    public async Task<Guid> MergeAsync(PeerContactContent content, ContactMergeSource source, IOdinContext odinContext,
        ContactExtData extData = null)
    {
        OdinValidationUtils.AssertNotNull(content, nameof(content));
        OdinValidationUtils.AssertIsTrue(!string.IsNullOrWhiteSpace(content.OdinId), "MergeAsync requires content.OdinId");
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        OdinValidationUtils.AssertIsValidOdinId(content.OdinId, out var odinId);
        content.OdinId = odinId.DomainName;
        var uniqueId = ToContactUniqueId(odinId);
        var writeContext = GetWriteContext(odinContext);

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var existing = await GetForWritingAsync(uniqueId, writeContext);
            if (existing == null)
            {
                // First write for this odinId. Project the peer-sourced fields onto a fresh
                // ContactContent (owner-owned fields stay unset) before persisting.
                var created = Merge(new ContactContent(), content);
                var (_, versionTag) = await WriteNewAsync(uniqueId, created, writeContext);

                // The create path writes content only; if the peer also brought ext_data, persist it in
                // a follow-up write now that the file exists (rare: a brand-new contact that already
                // carries bios). Content is unchanged on this second pass, so only the ext_data payload
                // is added.
                if (extData is { IsEmpty: false })
                {
                    var fresh = await GetForWritingAsync(uniqueId, writeContext);
                    if (fresh != null)
                    {
                        versionTag = await OverwriteAsync(fresh, content, source, extData, writeContext);
                    }
                }

                return versionTag;
            }

            try
            {
                return await OverwriteAsync(existing, content, source, extData, writeContext);
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.VersionTagMismatch)
            {
                logger.LogDebug("Contact merge race for {odinId} (attempt {attempt}/{maxAttempts})", odinId, attempt, maxAttempts);
            }
        }

        logger.LogWarning("Contact merge for {odinId} gave up after {attempts} attempts; reconcile will converge it",
            odinId, maxAttempts);
        return Guid.Empty;
    }

    private async Task<ServerFileHeader> GetForWritingAsync(Guid uniqueId, IOdinContext writeContext)
    {
        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = false,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = false
        };

        var match = await fileSystem.Query.GetFileByClientUniqueId(DriveId, uniqueId, options, writeContext);
        if (match == null)
        {
            return null;
        }

        var file = new InternalDriveFileId(DriveId, match.FileId);
        return await fileSystem.Storage.GetServerFileHeaderForWriting(file, writeContext);
    }

    private async Task<(Guid uniqueId, Guid versionTag)> WriteNewAsync(Guid uniqueId, ContactContent content,
        IOdinContext writeContext)
    {
        var file = await fileSystem.Storage.CreateInternalFileId(DriveId, writeContext);
        var keyHeader = KeyHeader.NewRandom16();
        var versionTag = SequentialGuid.CreateGuid();

        var fileMetadata = new FileMetadata(file)
        {
            AppData = new AppFileMetaData
            {
                FileType = ContactFileType,
                UniqueId = uniqueId,
                Tags = BuildTags(uniqueId, content),
                Content = EncryptContent(keyHeader, content)
            },
            IsEncrypted = true,
            VersionTag = versionTag,
            Payloads = []
        };

        var serverMetadata = new ServerMetadata
        {
            AccessControlList = AccessControlList.OwnerOnly,
            AllowDistribution = false
        };

        var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata,
            serverMetadata, writeContext);
        await fileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, writeContext, raiseEvent: true);

        return (uniqueId, serverFileHeader.FileMetadata.VersionTag.GetValueOrDefault(versionTag));
    }

    private async Task<Guid> OverwriteAsync(ServerFileHeader existing, PeerContactContent incoming, ContactMergeSource source,
        ContactExtData extData, IOdinContext writeContext)
    {
        var file = existing.FileMetadata.File;
        var storageKey = writeContext.PermissionsContext.GetDriveStorageKey(DriveId);
        var keyHeader = existing.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

        // Merge incoming over existing without clobbering fields the caller left null. Capture the set
        // of overwritten field values for the merge log before we replace the content.
        var existingContent = DecryptContent(keyHeader, existing.FileMetadata.AppData.Content);
        var overwrites = ContactMergeLog.ComputeOverwrites(existingContent, incoming);
        var merged = Merge(existingContent, incoming);

        // ext_data is peer-only and replaced wholesale: write it when the peer supplied non-empty
        // extended data; otherwise the stored ext_data payload (if any) is carried forward untouched.
        var writeExtData = extData is { IsEmpty: false };

        if (overwrites.Count == 0 && !writeExtData)
        {
            // Nothing to log (first-time fills / no-ops) and no ext_data change. Content-only update.
            // The content IV must still rotate — re-encrypting different plaintext under the same
            // key+IV is forbidden (the same rule UpdateBatchAsync enforces) — so re-wrap the file's
            // key header under the fresh IV.
            var contentKeyHeader = new KeyHeader { Iv = ByteArrayUtil.GetRndByteArray(16), AesKey = keyHeader.AesKey };
            existing.FileMetadata.AppData.Content = EncryptContent(contentKeyHeader, merged);
            existing.FileMetadata.AppData.Tags = BuildTags(existing.FileMetadata.AppData.UniqueId.GetValueOrDefault(), merged);
            existing.EncryptedKeyHeader = await fileSystem.Storage.EncryptKeyHeader(DriveId, contentKeyHeader, writeContext);
            await fileSystem.Storage.UpdateActiveFileHeader(file, existing, writeContext, raiseEvent: true);
            return existing.FileMetadata.VersionTag.GetValueOrDefault();
        }

        // A payload changed (merge_log to append and/or ext_data to replace): write the merged content
        // AND the affected payload(s) in ONE transaction so the change and its history land together (or
        // not at all). The merge_log is only read when there's an overwrite to append to it.
        var existingLog = overwrites.Count > 0
            ? await ReadMergeLogAsync(existing, keyHeader, writeContext)
            : null;
        return await WriteMergedAsync(existing, merged, keyHeader.AesKey, existingLog, overwrites, source, extData,
            existing.FileMetadata.VersionTag.GetValueOrDefault(), writeContext);
    }

    /// <summary>
    /// Atomically writes the merged content and—when present—the appended <c>merge_log</c> and/or the
    /// wholesale-replaced <c>ext_data</c> payload via a single
    /// <see cref="DriveStorageServiceBase.UpdateBatchAsync"/> commit (one version-tag advance, one change
    /// notification). The contact's AES key is reused; the content key-header IV is rotated (a hard
    /// requirement for encrypted updates), and each rewritten payload gets its own IV. Payloads this
    /// write doesn't touch (the profile image, and whichever of merge_log/ext_data isn't being written)
    /// are carried forward unchanged.
    /// </summary>
    private async Task<Guid> WriteMergedAsync(ServerFileHeader existing, ContactContent merged,
        SensitiveByteArray aesKey, List<ContactMergeLogEntry> existingLog, Dictionary<string, string> overwrites,
        ContactMergeSource source, ContactExtData extData, Guid currentVersionTag, IOdinContext writeContext)
    {
        var file = existing.FileMetadata.File;

        // 1) Merged content, re-encrypted under a fresh content IV.
        var contentKeyHeader = new KeyHeader { Iv = ByteArrayUtil.GetRndByteArray(16), AesKey = aesKey };
        var contentBase64 = EncryptContent(contentKeyHeader, merged);

        var rewritten = new List<PayloadDescriptor>();
        var instructions = new List<PayloadInstruction>();

        // 2) merge_log: appended + trimmed, when this merge overwrote a prior value.
        if (overwrites is { Count: > 0 })
        {
            var logBytes = ContactMergeLog.BuildUpdatedLog(existingLog, overwrites, source, UnixTimeUtc.Now());
            rewritten.Add(await StagePayloadAsync(file, ContactMergeLog.PayloadKey, ContactMergeLog.ContentType,
                logBytes, aesKey, writeContext));
            instructions.Add(new PayloadInstruction
                { Key = ContactMergeLog.PayloadKey, OperationType = PayloadUpdateOperationType.AppendOrOverwrite });
        }

        // 3) ext_data: peer-only, replaced wholesale, when the peer brought non-empty extended data.
        if (extData is { IsEmpty: false })
        {
            rewritten.Add(await StagePayloadAsync(file, ContactExtData.PayloadKey, ContactExtData.ContentType,
                extData.Serialize(), aesKey, writeContext));
            instructions.Add(new PayloadInstruction
                { Key = ContactExtData.PayloadKey, OperationType = PayloadUpdateOperationType.AppendOrOverwrite });
        }

        // Carry forward every payload this write isn't rewriting (e.g. the profile image, and whichever
        // of merge_log/ext_data wasn't touched). Payloads not named in PayloadInstruction are preserved.
        var carried = (existing.FileMetadata.Payloads ?? new List<PayloadDescriptor>())
            .Where(p => !rewritten.Any(r => p.KeyEquals(r.Key)))
            .ToList();

        // New metadata: merged content + the rewritten payload descriptors. VersionTag carries the
        // *current* tag (the optimistic-concurrency expectation); NewVersionTag is the tag after the write.
        var newMetadata = new FileMetadata(file)
        {
            AppData = new AppFileMetaData
            {
                FileType = ContactFileType,
                UniqueId = existing.FileMetadata.AppData.UniqueId,
                Tags = BuildTags(existing.FileMetadata.AppData.UniqueId.GetValueOrDefault(), merged),
                Content = contentBase64
            },
            IsEncrypted = true,
            VersionTag = currentVersionTag,
            Payloads = [.. carried, .. rewritten]
        };

        var manifest = new BatchUpdateManifest
        {
            NewVersionTag = SequentialGuid.CreateGuid(),
            KeyHeader = contentKeyHeader,
            FileMetadata = newMetadata,
            ServerMetadata = new ServerMetadata
            {
                AccessControlList = AccessControlList.OwnerOnly,
                AllowDistribution = false
            },
            PayloadInstruction = instructions
        };

        try
        {
            // Commits the header (content) and the staged payload(s) in a single transaction; payloads
            // were staged via WriteUploadStream, so they live in the Upload staging area.
            await fileSystem.Storage.UpdateBatchAsync(file, file, manifest, writeContext, markComplete: null,
                sourceArea: StagingArea.Upload);
            return manifest.NewVersionTag;
        }
        finally
        {
            await fileSystem.Storage.CleanupUploadTemporaryFiles(file, rewritten, writeContext);
        }
    }

    /// <summary>
    /// Encrypts <paramref name="plain"/> under the file AES key with a fresh per-payload IV, stages the
    /// ciphertext to the upload temp area, and returns its descriptor (ready for an AppendOrOverwrite
    /// instruction).
    /// </summary>
    private async Task<PayloadDescriptor> StagePayloadAsync(InternalDriveFileId file, string key, string contentType,
        byte[] plain, SensitiveByteArray aesKey, IOdinContext writeContext)
    {
        var iv = ByteArrayUtil.GetRndByteArray(16);
        var cipher = new KeyHeader { Iv = iv, AesKey = aesKey }.EncryptDataAes(plain);

        var uid = UnixTimeUtcUnique.Now();
        var extension = TenantPathManager.GetBasePayloadFileNameAndExtension(key, uid);
        var bytesWritten = await fileSystem.Storage.WriteUploadStream(file, extension, new MemoryStream(cipher), writeContext);

        return new PayloadDescriptor
        {
            Key = key,
            Uid = uid,
            Iv = iv,
            ContentType = contentType,
            BytesWritten = bytesWritten,
            LastModified = UnixTimeUtc.Now(),
            Thumbnails = new List<ThumbnailDescriptor>()
        };
    }

    /// <summary>
    /// Encrypts the image + its thumbnails under one fresh payload IV (the file's AES key), stages them,
    /// and commits the <see cref="ProfileImagePayloadKey"/> payload while preserving the contact's
    /// content (re-encrypted under a fresh content IV) and any other payloads (e.g. merge_log).
    /// </summary>
    private async Task<Guid> WriteImagePayloadAsync(ServerFileHeader existing, SetContactImageRequest request,
        IOdinContext writeContext)
    {
        var file = existing.FileMetadata.File;
        var storageKey = writeContext.PermissionsContext.GetDriveStorageKey(DriveId);
        var keyHeader = existing.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
        var aesKey = keyHeader.AesKey;

        // Preserve the existing content; the content IV must rotate on every update.
        var content = DecryptContent(keyHeader, existing.FileMetadata.AppData.Content);
        var contentKeyHeader = new KeyHeader { Iv = ByteArrayUtil.GetRndByteArray(16), AesKey = aesKey };
        var contentBase64 = EncryptContent(contentKeyHeader, content);

        // The image + thumbnails arrive already encrypted by the client (under the file's AES key and
        // request.Iv); the server stores the ciphertext verbatim and records the IV for later decryption.
        var uid = UnixTimeUtcUnique.Now();

        var imageExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(ProfileImagePayloadKey, uid);
        var imageBytesWritten = await fileSystem.Storage.WriteUploadStream(file, imageExtension,
            new MemoryStream(request.Content), writeContext);

        var thumbnailDescriptors = new List<ThumbnailDescriptor>();
        foreach (var thumb in request.Thumbnails ?? [])
        {
            OdinValidationUtils.AssertIsTrue(thumb.Content is { Length: > 0 }, "thumbnail content is required");
            OdinValidationUtils.AssertIsTrue(thumb.PixelWidth > 0 && thumb.PixelHeight > 0, "thumbnail dimensions are required");

            var thumbExtension = TenantPathManager.GetThumbnailFileNameAndExtension(
                ProfileImagePayloadKey, uid, thumb.PixelWidth, thumb.PixelHeight);
            var thumbBytesWritten = await fileSystem.Storage.WriteUploadStream(file, thumbExtension,
                new MemoryStream(thumb.Content), writeContext);

            thumbnailDescriptors.Add(new ThumbnailDescriptor
            {
                PixelWidth = thumb.PixelWidth,
                PixelHeight = thumb.PixelHeight,
                ContentType = thumb.ContentType,
                BytesWritten = thumbBytesWritten
            });
        }

        var imageDescriptor = new PayloadDescriptor
        {
            Key = ProfileImagePayloadKey,
            Uid = uid,
            Iv = request.Iv,
            ContentType = request.ContentType,
            BytesWritten = imageBytesWritten,
            LastModified = UnixTimeUtc.Now(),
            Thumbnails = thumbnailDescriptors
        };

        var manifest = BuildContentManifest(existing, contentKeyHeader, contentBase64, content,
            [.. CarryForwardPayloads(existing, ProfileImagePayloadKey), imageDescriptor],
            [new PayloadInstruction { Key = ProfileImagePayloadKey, OperationType = PayloadUpdateOperationType.AppendOrOverwrite }]);

        var staged = new List<PayloadDescriptor> { imageDescriptor };
        try
        {
            await fileSystem.Storage.UpdateBatchAsync(file, file, manifest, writeContext, markComplete: null,
                sourceArea: StagingArea.Upload);
            return manifest.NewVersionTag;
        }
        finally
        {
            await fileSystem.Storage.CleanupUploadTemporaryFiles(file, staged, writeContext);
        }
    }

    /// <summary>
    /// Removes the <see cref="ProfileImagePayloadKey"/> payload, preserving content (re-encrypted under a
    /// fresh content IV) and any other payloads.
    /// </summary>
    private async Task<Guid> RemoveImagePayloadAsync(ServerFileHeader existing, IOdinContext writeContext)
    {
        var file = existing.FileMetadata.File;
        var storageKey = writeContext.PermissionsContext.GetDriveStorageKey(DriveId);
        var keyHeader = existing.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

        var content = DecryptContent(keyHeader, existing.FileMetadata.AppData.Content);
        var contentKeyHeader = new KeyHeader { Iv = ByteArrayUtil.GetRndByteArray(16), AesKey = keyHeader.AesKey };
        var contentBase64 = EncryptContent(contentKeyHeader, content);

        var manifest = BuildContentManifest(existing, contentKeyHeader, contentBase64, content,
            CarryForwardPayloads(existing, ProfileImagePayloadKey),
            [new PayloadInstruction { Key = ProfileImagePayloadKey, OperationType = PayloadUpdateOperationType.DeletePayload }]);

        await fileSystem.Storage.UpdateBatchAsync(file, file, manifest, writeContext, markComplete: null,
            sourceArea: StagingArea.Upload);
        return manifest.NewVersionTag;
    }

    /// <summary>
    /// Builds a <see cref="BatchUpdateManifest"/> that rewrites the contact's content header (fresh
    /// content IV / version tag) with the given payload set and instructions.
    /// </summary>
    private BatchUpdateManifest BuildContentManifest(ServerFileHeader existing, KeyHeader contentKeyHeader,
        string contentBase64, ContactContent content, List<PayloadDescriptor> payloads,
        List<PayloadInstruction> instructions)
    {
        var file = existing.FileMetadata.File;
        return new BatchUpdateManifest
        {
            NewVersionTag = SequentialGuid.CreateGuid(),
            KeyHeader = contentKeyHeader,
            FileMetadata = new FileMetadata(file)
            {
                AppData = new AppFileMetaData
                {
                    FileType = ContactFileType,
                    UniqueId = existing.FileMetadata.AppData.UniqueId,
                    Tags = BuildTags(existing.FileMetadata.AppData.UniqueId.GetValueOrDefault(), content),
                    Content = contentBase64
                },
                IsEncrypted = true,
                VersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault(),
                Payloads = payloads
            },
            ServerMetadata = new ServerMetadata { AccessControlList = AccessControlList.OwnerOnly, AllowDistribution = false },
            PayloadInstruction = instructions
        };
    }

    private static List<PayloadDescriptor> CarryForwardPayloads(ServerFileHeader existing, string excludeKey)
    {
        return (existing.FileMetadata.Payloads ?? new List<PayloadDescriptor>())
            .Where(p => !p.KeyEquals(excludeKey))
            .ToList();
    }

    private static ContactWriteResult VersionConflictResult(Guid uniqueId, ServerFileHeader existing,
        Guid currentVersionTag, IOdinContext odinContext)
    {
        return new ContactWriteResult
        {
            Outcome = ContactWriteOutcome.VersionConflict,
            UniqueId = uniqueId,
            VersionTag = currentVersionTag,
            CurrentOnConflict = DriveFileUtility.CreateClientFileHeader(existing, odinContext)
        };
    }

    private async Task<List<ContactMergeLogEntry>> ReadMergeLogAsync(ServerFileHeader header, KeyHeader keyHeader,
        IOdinContext writeContext)
    {
        var descriptor = header.FileMetadata.Payloads?.FirstOrDefault(p => p.KeyEquals(ContactMergeLog.PayloadKey));
        if (descriptor == null)
        {
            return new List<ContactMergeLogEntry>();
        }

        using var stream = await fileSystem.Storage.GetPayloadStreamAsync(
            header.FileMetadata.File, ContactMergeLog.PayloadKey, null, writeContext);
        if (stream == null)
        {
            return new List<ContactMergeLogEntry>();
        }

        using var ms = new MemoryStream();
        await stream.Stream.CopyToAsync(ms);
        var cipher = ms.ToArray();
        if (cipher.Length == 0)
        {
            return new List<ContactMergeLogEntry>();
        }

        // Payloads are encrypted with the file's AES key under their own per-payload IV.
        var payloadKeyHeader = new KeyHeader { Iv = descriptor.Iv, AesKey = keyHeader.AesKey };
        var plain = payloadKeyHeader.Decrypt(cipher);
        return ContactMergeLog.Deserialize(plain);
    }

    private static List<Guid> BuildTags(Guid uniqueId, ContactContent content)
    {
        // Matches odin-js: tag the file with the identity key so it can be found by odinId.
        return string.IsNullOrWhiteSpace(content.OdinId)
            ? []
            : [uniqueId];
    }

    private static string EncryptContent(KeyHeader keyHeader, ContactContent content)
    {
        // odin-js contract / byte-compat: the value-objects are always present (rendered as {} when
        // empty), so a contact never loses a key across create/update/merge regardless of which fields
        // are set. Reads are unaffected; existing data gains the objects on its next write.
        content.Location ??= new ContactLocation();
        content.Phone ??= new ContactPhone();
        content.Email ??= new ContactEmail();
        content.Birthday ??= new ContactBirthday();

        var json = JsonSerializer.Serialize(content, ContentSerializerOptions);
        var cipher = keyHeader.EncryptDataAes(json.ToUtf8ByteArray());
        return cipher.ToBase64();
    }

    private static ContactContent DecryptContent(KeyHeader keyHeader, string storedContent)
    {
        if (string.IsNullOrEmpty(storedContent))
        {
            return new ContactContent();
        }

        var plain = keyHeader.Decrypt(Convert.FromBase64String(storedContent));
        return JsonSerializer.Deserialize<ContactContent>(plain.ToStringFromUtf8Bytes(), ContentSerializerOptions)
               ?? new ContactContent();
    }

    private static ContactContent Merge(ContactContent existing, PeerContactContent incoming)
    {
        existing ??= new ContactContent();
        incoming ??= new PeerContactContent();

        var merged = new ContactContent
        {
            OdinId = Coalesce(incoming.OdinId, existing.OdinId),
            Source = Coalesce(incoming.Source, existing.Source),
            Name = MergeName(existing.Name, incoming.Name),
            Location = MergeLocation(existing.Location, incoming.Location),
            Phone = MergePhone(existing.Phone, incoming.Phone),
            Email = MergeEmail(existing.Email, incoming.Email),
            Birthday = MergeBirthday(existing.Birthday, incoming.Birthday),
            ShortBio = Coalesce(incoming.ShortBio, existing.ShortBio),
            Nickname = Coalesce(incoming.Nickname, existing.Nickname),
            Status = Coalesce(incoming.Status, existing.Status),
            Link = Coalesce(incoming.Link, existing.Link),
            Social = MergeSocial(existing.Social, incoming.Social),

            // Owner-owned fields: preserved by default. A peer/enrichment write supplies a plain
            // PeerContactContent, which by construction carries none of these — so it can never
            // overwrite them. Only the owner API path passes a full ContactContent and may change them.
            IsEmergencyContact = existing.IsEmergencyContact
        };

        if (incoming is ContactContent owner)
        {
            merged.IsEmergencyContact = owner.IsEmergencyContact;
        }

        return merged;
    }

    /// <summary>
    /// Field merge rule shared by every contact field: an incoming value that is null or whitespace
    /// means "leave the existing value alone", never "clear it". Returns null only when neither side
    /// has a real value (so empty value-objects collapse away rather than being stored).
    /// </summary>
    private static string Coalesce(string incoming, string existing)
    {
        if (!string.IsNullOrWhiteSpace(incoming)) return incoming;
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        return null;
    }

    private static ContactName MergeName(ContactName existing, ContactName incoming)
    {
        if (incoming == null) return existing;
        if (existing == null) return incoming;
        return new ContactName
        {
            DisplayName = Coalesce(incoming.DisplayName, existing.DisplayName),
            GivenName = Coalesce(incoming.GivenName, existing.GivenName),
            AdditionalName = Coalesce(incoming.AdditionalName, existing.AdditionalName),
            Surname = Coalesce(incoming.Surname, existing.Surname)
        };
    }

    private static ContactLocation MergeLocation(ContactLocation existing, ContactLocation incoming)
    {
        if (incoming == null) return existing;
        if (existing == null) return incoming;
        return new ContactLocation
        {
            City = Coalesce(incoming.City, existing.City),
            Country = Coalesce(incoming.Country, existing.Country)
        };
    }

    private static ContactPhone MergePhone(ContactPhone existing, ContactPhone incoming)
    {
        var number = Coalesce(incoming?.Number, existing?.Number);
        return number == null ? null : new ContactPhone { Number = number };
    }

    private static ContactEmail MergeEmail(ContactEmail existing, ContactEmail incoming)
    {
        var email = Coalesce(incoming?.Email, existing?.Email);
        return email == null ? null : new ContactEmail { Email = email };
    }

    private static ContactBirthday MergeBirthday(ContactBirthday existing, ContactBirthday incoming)
    {
        var date = Coalesce(incoming?.Date, existing?.Date);
        return date == null ? null : new ContactBirthday { Date = date };
    }

    /// <summary>
    /// Key-wise union of the GUID-keyed social/game handles: incoming wins per key, an empty incoming
    /// value leaves the existing one alone (matching the <see cref="Coalesce"/> rule), and the result
    /// collapses to null when nothing remains so the field is omitted rather than stored as {}.
    /// </summary>
    private static Dictionary<string, string> MergeSocial(
        Dictionary<string, string> existing, Dictionary<string, string> incoming)
    {
        if (incoming == null) return existing;
        if (existing == null) return incoming;

        var merged = new Dictionary<string, string>(existing);
        foreach (var (key, value) in incoming)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                merged[key] = value;
            }
        }

        return merged.Count == 0 ? null : merged;
    }

}
