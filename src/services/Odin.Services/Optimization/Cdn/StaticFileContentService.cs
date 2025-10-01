using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Services.Optimization.Cdn;

// Filenames (now used as db keys):
// - sitedata.json: https://frodo.dotyou.cloud
// - public_image.json: https://frodo.dotyou.cloud/pub/image
// - public_profile.json: https://frodo.dotyou.cloud/pub/profile
// - public.json: ???

public enum CrossOriginBehavior
{
    /// <summary>
    /// Make no changes to the Access-Control-Allow-Origin header
    /// </summary>
    Default = 0,

    /// <summary>
    /// Updates Access-Control-Allow-Origin header to * when the corresponding file is served.
    /// </summary>
    AllowAllOrigins = 1,

    //Whitelist = 2,
}

public class StaticFileContentService(ILogger<StaticFileContentService> logger, StandardFileSystem fileSystem, IdentityDatabase db)
{
    private static readonly SingleKeyValueStorage StaticFileConfigStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse("3609449a-2f7f-4111-b300-3408a920aa2e"));

    public async Task<StaticFilePublishResult> PublishAsync(
        string filename,
        StaticFileConfiguration config,
        List<QueryParamSection> sections,
        IOdinContext odinContext)
    {
        

        //
        //TODO: optimize we need update this method to serialize in small chunks and write to stream instead of building a huge array of everything then serialization
        //

        //Note: I need to add a permission that better describes that we only wnt this done when the owner is in full
        //admin mode, not just from an app.  master key indicates you're in full admin mode
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.PublishStaticContent);
        foreach (var s in sections)
        {
            s.AssertIsValid();
        }

        var result = new StaticFilePublishResult()
        {
            Filename = filename,
            SectionResults = new List<SectionPublishResult>()
        };

        var sectionOutputList = new List<SectionOutput>();

        var timestamp = new SectionOutput
        {
            Name = "_ts:" + DateTimeOffset.Now.ToString("O"),
            Files = []
        };
        sectionOutputList.Add(timestamp);


        foreach (var section in sections)
        {
            var qp = section.QueryParams;
            var driveId = qp.TargetDrive.Alias;

            var options = new QueryBatchResultOptions()
            {
                IncludeHeaderContent = section.ResultOptions.IncludeHeaderContent,
                ExcludePreviewThumbnail = section.ResultOptions.ExcludePreviewThumbnail,
                Cursor = null, //TODO?
                MaxRecords = int.MaxValue //TODO: Consider
            };

            var results = await fileSystem.Query.GetBatch(driveId, qp, options,odinContext);
            var filteredHeaders = Filter(results.SearchResults);

            var sectionOutput = new SectionOutput()
            {
                Name = section.Name,
                Files = new List<StaticFile>()
            };
            sectionOutputList.Add(sectionOutput);

            foreach (var fileHeader in filteredHeaders)
            {
                var thumbnails = new List<ThumbnailContent>();
                var internalFileId = new InternalDriveFileId()
                {
                    FileId = fileHeader.FileId,
                    DriveId = driveId
                };

                var payloads = new List<PayloadStaticFileResponse>();
                if (section.ResultOptions.PayloadKeys?.Any() ?? false)
                {
                    foreach (var pd in fileHeader.FileMetadata.Payloads)
                    {
                        if (pd.Key == null || !section.ResultOptions.PayloadKeys.Contains(pd.Key))
                        {
                            continue;
                        }

                        using var ps = await fileSystem.Storage.GetPayloadStreamAsync(internalFileId, pd.Key, null,odinContext);
                        payloads.Add(new PayloadStaticFileResponse()
                        {
                            Key = ps.Key,
                            ContentType = ps.ContentType,
                            Data = ps.Stream.ToByteArray().ToBase64()
                        });
                    }
                }

                sectionOutput.Files.Add(new StaticFile()
                {
                    Header = fileHeader,
                    AdditionalThumbnails = thumbnails,
                    Payloads = payloads
                });
            }

            result.SectionResults.Add(new SectionPublishResult()
            {
                Name = sectionOutput.Name,
                FileCount = sectionOutput.Files.Count
            });
        }

        var data = OdinSystemSerializer.Serialize(sectionOutputList);

        config.ContentType = MediaTypeNames.Application.Json;
        config.LastModified = UnixTimeUtc.Now();

        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            await StaticFileConfigStorage.UpsertAsync(db.KeyValueCached, GetConfigKey(filename), config);
            await StaticFileConfigStorage.UpsertBytesAsync(db.KeyValueCached, GetDataKey(filename), data.ToUtf8ByteArray());
            tx.Commit();
        }

        logger.LogDebug("Wrote static file {Filename}", filename);

        return result;
    }

    public async Task PublishProfileImageAsync(string image64, string contentType)
    {
        var filename = StaticFileConstants.ProfileImageFileName;
        var imageBytes = Convert.FromBase64String(image64);

        var config = new StaticFileConfiguration
        {
            ContentType = contentType,
            LastModified = UnixTimeUtc.Now(),
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        await using var tx = await db.BeginStackedTransactionAsync();
        await StaticFileConfigStorage.UpsertAsync(db.KeyValueCached, GetConfigKey(filename), config);
        await StaticFileConfigStorage.UpsertBytesAsync(db.KeyValueCached, GetDataKey(filename), imageBytes);
        tx.Commit();
    }

    public async Task PublishProfileCardAsync(string json)
    {
        var filename = StaticFileConstants.PublicProfileCardFileName;

        var config = new StaticFileConfiguration
        {
            LastModified = UnixTimeUtc.Now(),
            ContentType = MediaTypeNames.Application.Json,
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        await using var tx = await db.BeginStackedTransactionAsync();
        await StaticFileConfigStorage.UpsertAsync(db.KeyValueCached, GetConfigKey(filename), config);
        await StaticFileConfigStorage.UpsertBytesAsync(db.KeyValueCached, GetDataKey(filename), json.ToUtf8ByteArray());
        tx.Commit();
    }

    private GuidId GetConfigKey(string filename)
    {
        return new GuidId(ByteArrayUtil.ReduceSHA256Hash(filename.ToLower()));
    }

    private GuidId GetDataKey(string filename)
    {
        return new GuidId(ByteArrayUtil.ReduceSHA256Hash(filename.ToLower() + "_data"));
    }


    public async Task<(StaticFileConfiguration config, bool fileExists, byte[] bytes)> GetStaticFileStreamAsync(
        string filename,
        UnixTimeUtc? ifModifiedSince = null)
    {
        var config = await StaticFileConfigStorage.GetAsync<StaticFileConfiguration>(db.KeyValueCached, GetConfigKey(filename));

        if (config == null)
        {
            return (null, false, null);
        }

        if (ifModifiedSince != null) //I was asked to check...
        {
            bool wasModified = config.LastModified > ifModifiedSince;
            if (!wasModified)
            {
                return (config, fileExists: true, null);
            }
        }

        var bytes = await StaticFileConfigStorage.GetBytesAsync(db.KeyValueCached, GetDataKey(filename));
        if (bytes == null)
        {
            return (config, false, null);
        }

        return (config, fileExists: true, bytes);
    }

    private IEnumerable<SharedSecretEncryptedFileHeader> Filter(IEnumerable<SharedSecretEncryptedFileHeader> headers)
    {
        return headers.Where(r =>
            r.FileState == FileState.Active &&
            r.FileMetadata.IsEncrypted == false &&
            r.ServerMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Anonymous);
    }
}