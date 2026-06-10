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

    /// <summary>
    /// Create-or-update a contact.
    /// <list type="bullet">
    /// <item>With an odinId → keyed on <see cref="ToContactUniqueId"/>; updates the same file in place,
    /// merging new content over existing (does not clobber fields the caller left null).</item>
    /// <item>Without an odinId → always creates a new file under a random unique id (no deterministic key
    /// exists; resolving duplicates is deferred).</item>
    /// </list>
    /// On update, the caller's <paramref name="versionTag"/> is checked for optimistic concurrency; a
    /// stale tag yields a non-success result carrying the current header for a 409 response.
    ///
    /// <para>
    /// When an update overwrites existing non-empty field values, the old values are appended to the
    /// contact's <c>merge_log</c> payload (tagged with <paramref name="source"/>).
    /// </para>
    /// </summary>
    public async Task<ContactUpsertResult> UpsertAsync(ContactContent content, Guid? versionTag, IOdinContext odinContext,
        ContactMergeSource source = ContactMergeSource.Api)
    {
        OdinValidationUtils.AssertNotNull(content, nameof(content));
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        OdinId? odinId = null;
        Guid uniqueId;
        if (!string.IsNullOrWhiteSpace(content.OdinId))
        {
            // Any syntactically valid domain is accepted; no liveness check.
            OdinValidationUtils.AssertIsValidOdinId(content.OdinId, out var parsed);
            odinId = parsed;
            content.OdinId = parsed.DomainName; // normalize
            uniqueId = ToContactUniqueId(parsed);
        }
        else
        {
            uniqueId = Guid.NewGuid();
        }

        var writeContext = OdinContextUpgrades.UpgradeToByPassAclCheck(Drive, DrivePermission.ReadWrite, odinContext);

        var existing = odinId.HasValue ? await GetForWritingAsync(uniqueId, writeContext) : null;
        if (existing == null)
        {
            var (createdUniqueId, createdVersionTag) = await WriteNewAsync(uniqueId, content, writeContext);
            return new ContactUpsertResult { Success = true, UniqueId = createdUniqueId, VersionTag = createdVersionTag };
        }

        // Optimistic concurrency: the caller must have read the current version.
        var currentVersionTag = existing.FileMetadata.VersionTag.GetValueOrDefault();
        if (versionTag.GetValueOrDefault() != currentVersionTag)
        {
            logger.LogDebug("Contact upsert version conflict for uid {uid} (caller {caller} vs current {current})",
                uniqueId, versionTag, currentVersionTag);

            return new ContactUpsertResult
            {
                Success = false,
                UniqueId = uniqueId,
                VersionTag = currentVersionTag,
                CurrentOnConflict = DriveFileUtility.CreateClientFileHeader(existing, odinContext)
            };
        }

        var newVersionTag = await OverwriteAsync(existing, content, source, writeContext);
        return new ContactUpsertResult { Success = true, UniqueId = uniqueId, VersionTag = newVersionTag };
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
        var writeContext = OdinContextUpgrades.UpgradeToByPassAclCheck(Drive, DrivePermission.ReadWrite, odinContext);

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

        var writeContext = OdinContextUpgrades.UpgradeToByPassAclCheck(Drive, DrivePermission.ReadWrite, odinContext);
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
    /// Server-internal create-or-merge for a contact that carries an odinId (used by enrichment and the
    /// lifecycle handlers). Unlike the client <see cref="UpsertAsync"/>, this does <b>not</b> do
    /// client-versionTag optimistic concurrency — it always merges over the current file, retrying once
    /// if a concurrent write advances the version mid-merge (the merge is idempotent; reconcile
    /// converges any straggler). Overwritten values are logged to <c>merge_log</c> tagged with
    /// <paramref name="source"/>.
    /// </summary>
    public async Task<Guid> MergeAsync(ContactContent content, ContactMergeSource source, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(content, nameof(content));
        OdinValidationUtils.AssertIsTrue(!string.IsNullOrWhiteSpace(content.OdinId), "MergeAsync requires content.OdinId");
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

        OdinValidationUtils.AssertIsValidOdinId(content.OdinId, out var odinId);
        content.OdinId = odinId.DomainName;
        var uniqueId = ToContactUniqueId(odinId);
        var writeContext = OdinContextUpgrades.UpgradeToByPassAclCheck(Drive, DrivePermission.ReadWrite, odinContext);

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var existing = await GetForWritingAsync(uniqueId, writeContext);
            if (existing == null)
            {
                var (_, versionTag) = await WriteNewAsync(uniqueId, content, writeContext);
                return versionTag;
            }

            try
            {
                return await OverwriteAsync(existing, content, source, writeContext);
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.VersionTagMismatch && attempt < maxAttempts)
            {
                logger.LogDebug("Contact merge race for {odinId}; re-reading and retrying", odinId);
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

    private async Task<Guid> OverwriteAsync(ServerFileHeader existing, ContactContent incoming, ContactMergeSource source,
        IOdinContext writeContext)
    {
        var file = existing.FileMetadata.File;
        var storageKey = writeContext.PermissionsContext.GetDriveStorageKey(DriveId);
        var keyHeader = existing.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

        // Merge incoming over existing without clobbering fields the caller left null. Capture the set
        // of overwritten field values for the merge log before we replace the content.
        var existingContent = DecryptContent(keyHeader, existing.FileMetadata.AppData.Content);
        var overwrites = ContactMergeLog.ComputeOverwrites(existingContent, incoming);
        var merged = Merge(existingContent, incoming);

        if (overwrites.Count == 0)
        {
            // Nothing to log (first-time fills / no-ops). Content-only update; no history to keep.
            existing.FileMetadata.AppData.Content = EncryptContent(keyHeader, merged);
            existing.FileMetadata.AppData.Tags = BuildTags(existing.FileMetadata.AppData.UniqueId.GetValueOrDefault(), merged);
            await fileSystem.Storage.UpdateActiveFileHeader(file, existing, writeContext, raiseEvent: true);
            return existing.FileMetadata.VersionTag.GetValueOrDefault();
        }

        // History to keep: write the merged content AND the appended merge_log payload in ONE
        // transaction so the change and its history land together (or not at all).
        var existingLog = await ReadMergeLogAsync(existing, keyHeader, writeContext);
        return await WriteContentWithMergeLogAsync(existing, merged, keyHeader.AesKey, existingLog, overwrites, source,
            existing.FileMetadata.VersionTag.GetValueOrDefault(), writeContext);
    }

    /// <summary>
    /// Atomically writes the merged content and the appended <c>merge_log</c> payload via a single
    /// <see cref="DriveStorageServiceBase.UpdateBatchAsync"/> commit (one version-tag advance, one
    /// change notification). The contact's AES key is reused; the content key-header IV is rotated (a
    /// hard requirement for encrypted updates), and the merge_log payload gets its own IV.
    /// </summary>
    private async Task<Guid> WriteContentWithMergeLogAsync(ServerFileHeader existing, ContactContent merged,
        SensitiveByteArray aesKey, List<ContactMergeLogEntry> existingLog, Dictionary<string, string> overwrites,
        ContactMergeSource source, Guid currentVersionTag, IOdinContext writeContext)
    {
        var file = existing.FileMetadata.File;

        // 1) Merged content, re-encrypted under a fresh content IV.
        var contentIv = ByteArrayUtil.GetRndByteArray(16);
        var contentKeyHeader = new KeyHeader { Iv = contentIv, AesKey = aesKey };
        var contentBase64 = EncryptContent(contentKeyHeader, merged);

        // 2) Appended + trimmed merge log, encrypted under its own IV, staged to the upload temp area.
        var logBytes = ContactMergeLog.BuildUpdatedLog(existingLog, overwrites, source, UnixTimeUtc.Now());
        var logIv = ByteArrayUtil.GetRndByteArray(16);
        var logCipher = new KeyHeader { Iv = logIv, AesKey = aesKey }.EncryptDataAes(logBytes);

        var uid = UnixTimeUtcUnique.Now();
        var extension = TenantPathManager.GetBasePayloadFileNameAndExtension(ContactMergeLog.PayloadKey, uid);
        var bytesWritten = await fileSystem.Storage.WriteUploadStream(file, extension, new MemoryStream(logCipher), writeContext);

        var mergeLogDescriptor = new PayloadDescriptor
        {
            Key = ContactMergeLog.PayloadKey,
            Uid = uid,
            Iv = logIv,
            ContentType = ContactMergeLog.ContentType,
            BytesWritten = bytesWritten,
            LastModified = UnixTimeUtc.Now(),
            Thumbnails = new List<ThumbnailDescriptor>()
        };

        // 3) New metadata: merged content + the merge_log descriptor. VersionTag carries the *current*
        // tag (the optimistic-concurrency expectation); NewVersionTag is the tag after the write.
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
            Payloads = [mergeLogDescriptor]
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
            PayloadInstruction =
            [
                new PayloadInstruction
                {
                    Key = ContactMergeLog.PayloadKey,
                    OperationType = PayloadUpdateOperationType.AppendOrOverwrite
                }
            ]
        };

        var descriptors = new List<PayloadDescriptor> { mergeLogDescriptor };
        try
        {
            // Keeps existing payloads not named in the instructions (e.g. a future prfl_pic); commits
            // the header (content) and the merge_log payload in a single transaction.
            await fileSystem.Storage.UpdateBatchAsync(file, file, manifest, writeContext, markComplete: null);
            return manifest.NewVersionTag;
        }
        finally
        {
            await fileSystem.Storage.CleanupUploadTemporaryFiles(file, descriptors, writeContext);
        }
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

    private static System.Collections.Generic.List<Guid> BuildTags(Guid uniqueId, ContactContent content)
    {
        // Matches odin-js: tag the file with the identity key so it can be found by odinId.
        return string.IsNullOrWhiteSpace(content.OdinId)
            ? []
            : [uniqueId];
    }

    private static string EncryptContent(KeyHeader keyHeader, ContactContent content)
    {
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

    private static ContactContent Merge(ContactContent existing, ContactContent incoming)
    {
        existing ??= new ContactContent();
        incoming ??= new ContactContent();

        return new ContactContent
        {
            OdinId = incoming.OdinId ?? existing.OdinId,
            Name = MergeName(existing.Name, incoming.Name),
            Location = MergeLocation(existing.Location, incoming.Location),
            Phone = incoming.Phone ?? existing.Phone,
            Email = incoming.Email ?? existing.Email,
            Birthday = incoming.Birthday ?? existing.Birthday
        };
    }

    private static ContactName MergeName(ContactName existing, ContactName incoming)
    {
        if (incoming == null) return existing;
        if (existing == null) return incoming;
        return new ContactName
        {
            DisplayName = incoming.DisplayName ?? existing.DisplayName,
            GivenName = incoming.GivenName ?? existing.GivenName,
            AdditionalName = incoming.AdditionalName ?? existing.AdditionalName,
            Surname = incoming.Surname ?? existing.Surname
        };
    }

    private static ContactLocation MergeLocation(ContactLocation existing, ContactLocation incoming)
    {
        if (incoming == null) return existing;
        if (existing == null) return incoming;
        return new ContactLocation
        {
            City = incoming.City ?? existing.City,
            Country = incoming.Country ?? existing.Country
        };
    }

}
