using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
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
        return ToGuidId(odinId.DomainName);
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
    /// </summary>
    public async Task<ContactUpsertResult> UpsertAsync(ContactContent content, Guid? versionTag, IOdinContext odinContext)
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

        var newVersionTag = await OverwriteAsync(existing, content, writeContext);
        return new ContactUpsertResult { Success = true, UniqueId = uniqueId, VersionTag = newVersionTag };
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
    /// Trigger re-enrichment of a contact from the peer (connected) or public (not connected) profile.
    /// </summary>
    /// <remarks>
    /// Enrichment (the peer/public profile pull + merge, and its background job) is a separate part of
    /// the plan and is not yet built; this is the entry point the controller calls so the surface is in
    /// place. It asserts the permission and exits.
    /// </remarks>
    public Task SyncAsync(OdinId odinId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);
        logger.LogDebug("Contact sync requested for {odinId}; enrichment is not yet implemented (no-op).", odinId);
        return Task.CompletedTask;
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

    private async Task<Guid> OverwriteAsync(ServerFileHeader existing, ContactContent incoming, IOdinContext writeContext)
    {
        var storageKey = writeContext.PermissionsContext.GetDriveStorageKey(DriveId);
        var keyHeader = existing.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

        // Merge incoming over existing without clobbering fields the caller left null.
        var existingContent = DecryptContent(keyHeader, existing.FileMetadata.AppData.Content);
        var merged = Merge(existingContent, incoming);

        existing.FileMetadata.AppData.Content = EncryptContent(keyHeader, merged);
        existing.FileMetadata.AppData.Tags = BuildTags(existing.FileMetadata.AppData.UniqueId.GetValueOrDefault(), merged);

        await fileSystem.Storage.UpdateActiveFileHeader(existing.FileMetadata.File, existing, writeContext, raiseEvent: true);
        return existing.FileMetadata.VersionTag.GetValueOrDefault();
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

    private static Guid ToGuidId(string input)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var b = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        return new Guid(b);
    }
}
