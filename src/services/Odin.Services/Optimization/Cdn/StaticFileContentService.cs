using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;

namespace Odin.Services.Optimization.Cdn;

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

public class StaticFileContentService
{
    private readonly DriveManager _driveManager;
    private readonly StandardFileSystem _fileSystem;
    private readonly TenantContext _tenantContext;
    private readonly TenantSystemStorage _tenantSystemStorage;

    private readonly SingleKeyValueStorage _staticFileConfigStorage;
    private readonly DriveFileReaderWriter _driveFileReaderWriter;

    public StaticFileContentService(TenantContext tenantContext, TenantSystemStorage tenantSystemStorage,
        DriveManager driveManager, StandardFileSystem fileSystem, DriveFileReaderWriter driveFileReaderWriter)
    {
        _tenantContext = tenantContext;
        _tenantSystemStorage = tenantSystemStorage;

        _driveManager = driveManager;
        _fileSystem = fileSystem;
        _driveFileReaderWriter = driveFileReaderWriter;

        const string staticFileContextKey = "3609449a-2f7f-4111-b300-3408a920aa2e";
        _staticFileConfigStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(staticFileContextKey));
    }

    public async Task<StaticFilePublishResult> Publish(string filename, StaticFileConfiguration config,
        List<QueryParamSection> sections, IOdinContext odinContext, IdentityDatabase db)
    {
        //
        //TODO: optimize we need update this method to serialize in small chunks and write to stream instead of building a huge array of everything then serialization
        //

        //Note: I need to add a permission that better describes that we only wnt this done when the owner is in full
        //admin mode, not just from an app.  master key indicates you're in full admin mode
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.PublishStaticContent);
        string targetFolder = await EnsurePath();
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

        foreach (var section in sections)
        {
            var qp = section.QueryParams;
            var driveId = (await _driveManager.GetDriveIdByAlias(qp.TargetDrive, db, true)).GetValueOrDefault();

            var options = new QueryBatchResultOptions()
            {
                IncludeHeaderContent = section.ResultOptions.IncludeHeaderContent,
                ExcludePreviewThumbnail = section.ResultOptions.ExcludePreviewThumbnail,
                Cursor = null, //TODO?
                MaxRecords = int.MaxValue //TODO: Consider
            };

            var results = await _fileSystem.Query.GetBatch(driveId, qp, options,odinContext, db);
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

                        var ps = await _fileSystem.Storage.GetPayloadStream(internalFileId, pd.Key, null,odinContext, db);
                        try
                        {
                            payloads.Add(new PayloadStaticFileResponse()
                            {
                                Key = ps.Key,
                                ContentType = ps.ContentType,
                                Data = ps.Stream.ToByteArray().ToBase64()
                            });
                        }
                        finally
                        {
                            // TODO: PayloadStream should probably have some sort of dtor or
                            // know if it needs to dispose if the stream it is holding on to
                            if (ps.Stream != null)
                            {
                                await ps.Stream.DisposeAsync();
                            }

                            ps = null;
                        }
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

        var ms = new MemoryStream();
        await OdinSystemSerializer.Serialize(ms, sectionOutputList, sectionOutputList.GetType());
        string finalTargetPath = Path.Combine(targetFolder, filename);
        ms.Seek(0L, SeekOrigin.Begin);
        var bytesWritten = _driveFileReaderWriter.WriteStream(finalTargetPath, ms);

        config.ContentType = MediaTypeNames.Application.Json;
        config.LastModified = UnixTimeUtc.Now();

        _staticFileConfigStorage.Upsert(db, GetConfigKey(filename), config);

        return result;
    }

    public async Task PublishProfileImage(string image64, string contentType, IdentityDatabase db)
    {
        string filename = StaticFileConstants.ProfileImageFileName;
        string targetFolder = await EnsurePath();

        string finalTargetPath = Path.Combine(targetFolder, filename);
        var imageBytes = Convert.FromBase64String(image64);
        await _driveFileReaderWriter.WriteAllBytes(finalTargetPath, imageBytes);

        var config = new StaticFileConfiguration()
        {
            ContentType = contentType,
            LastModified = UnixTimeUtc.Now(),
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        _staticFileConfigStorage.Upsert(db, GetConfigKey(filename), config);

        await Task.CompletedTask;
    }

    public async Task PublishProfileCard(string json, IdentityDatabase db)
    {
        string filename = StaticFileConstants.PublicProfileCardFileName;
        string targetFolder = await EnsurePath();

        string finalTargetPath = Path.Combine(targetFolder, filename);
        await _driveFileReaderWriter.WriteString(finalTargetPath, json);

        var config = new StaticFileConfiguration()
        {
            LastModified = UnixTimeUtc.Now(),
            ContentType = MediaTypeNames.Application.Json,
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        config.ContentType = MediaTypeNames.Application.Json;
        _staticFileConfigStorage.Upsert(db, GetConfigKey(filename), config);

        await Task.CompletedTask;
    }

    private GuidId GetConfigKey(string filename)
    {
        return new GuidId(ByteArrayUtil.ReduceSHA256Hash(filename.ToLower()));
    }

    public async Task<(StaticFileConfiguration config, bool fileExists, Stream fileStream)> GetStaticFileStream(string filename,
        IdentityDatabase db,
        UnixTimeUtc? ifModifiedSince = null)
    {
        var config = _staticFileConfigStorage.Get<StaticFileConfiguration>(db, GetConfigKey(filename));
        var targetFile = Path.Combine(_tenantContext.StorageConfig.StaticFileStoragePath, filename);

        if (config == null || !File.Exists(targetFile))
        {
            return (null, false, Stream.Null);
        }

        if (ifModifiedSince != null) //I was asked to check...
        {
            bool wasModified = config.LastModified > ifModifiedSince;
            if (!wasModified)
            {
                return (config, fileExists: true, Stream.Null);
            }
        }

        var fileStream = await _driveFileReaderWriter.OpenStreamForReading(targetFile);
        return (config, fileExists: true, fileStream);
    }

    private async Task<string> EnsurePath()
    {
        string targetFolder = _tenantContext.StorageConfig.StaticFileStoragePath;
        await _driveFileReaderWriter.CreateDirectory(targetFolder);
        return targetFolder;
    }

    private IEnumerable<SharedSecretEncryptedFileHeader> Filter(IEnumerable<SharedSecretEncryptedFileHeader> headers)
    {
        return headers.Where(r =>
            r.FileState == FileState.Active &&
            r.FileMetadata.IsEncrypted == false &&
            r.ServerMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Anonymous);
    }
}