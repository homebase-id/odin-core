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
using Odin.Core.Storage.Database.Identity.Table;
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
    private readonly SingleKeyValueStorage _staticFileConfigStorage;
    private readonly DriveFileReaderWriter _driveFileReaderWriter;
    private readonly TableKeyValue _tableKeyValue;

    public StaticFileContentService(
        TenantContext tenantContext,
        DriveManager driveManager,
        StandardFileSystem fileSystem,
        DriveFileReaderWriter driveFileReaderWriter,
        TableKeyValue tableKeyValue)
    {
        _tenantContext = tenantContext;

        _driveManager = driveManager;
        _fileSystem = fileSystem;
        _driveFileReaderWriter = driveFileReaderWriter;
        _tableKeyValue = tableKeyValue;

        const string staticFileContextKey = "3609449a-2f7f-4111-b300-3408a920aa2e";
        _staticFileConfigStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(staticFileContextKey));
    }

    public async Task<StaticFilePublishResult> PublishAsync(string filename, StaticFileConfiguration config,
        List<QueryParamSection> sections, IOdinContext odinContext)
    {
        

        //
        //TODO: optimize we need update this method to serialize in small chunks and write to stream instead of building a huge array of everything then serialization
        //

        //Note: I need to add a permission that better describes that we only wnt this done when the owner is in full
        //admin mode, not just from an app.  master key indicates you're in full admin mode
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.PublishStaticContent);
        string targetFolder = EnsurePath();
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
            var driveId = (await _driveManager.GetDriveIdByAliasAsync(qp.TargetDrive, true)).GetValueOrDefault();

            var options = new QueryBatchResultOptions()
            {
                IncludeHeaderContent = section.ResultOptions.IncludeHeaderContent,
                ExcludePreviewThumbnail = section.ResultOptions.ExcludePreviewThumbnail,
                Cursor = null, //TODO?
                MaxRecords = int.MaxValue //TODO: Consider
            };

            var results = await _fileSystem.Query.GetBatch(driveId, qp, options,odinContext);
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

                        using var ps = await _fileSystem.Storage.GetPayloadStreamAsync(internalFileId, pd.Key, null,odinContext);
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

        var ms = new MemoryStream();
        await OdinSystemSerializer.Serialize(ms, sectionOutputList, sectionOutputList.GetType());
        string finalTargetPath = Path.Combine(targetFolder, filename);
        ms.Seek(0L, SeekOrigin.Begin);
        var bytesWritten = _driveFileReaderWriter.WriteStreamAsync(finalTargetPath, ms);

        config.ContentType = MediaTypeNames.Application.Json;
        config.LastModified = UnixTimeUtc.Now();

        await _staticFileConfigStorage.UpsertAsync(_tableKeyValue, GetConfigKey(filename), config);

        return result;
    }

    public async Task PublishProfileImageAsync(string image64, string contentType)
    {
        

        string filename = StaticFileConstants.ProfileImageFileName;
        string targetFolder = EnsurePath();

        string finalTargetPath = Path.Combine(targetFolder, filename);
        var imageBytes = Convert.FromBase64String(image64);
        await _driveFileReaderWriter.WriteAllBytesAsync(finalTargetPath, imageBytes);

        var config = new StaticFileConfiguration()
        {
            ContentType = contentType,
            LastModified = UnixTimeUtc.Now(),
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        await _staticFileConfigStorage.UpsertAsync(_tableKeyValue, GetConfigKey(filename), config);
    }

    public async Task PublishProfileCardAsync(string json)
    {
        

        string filename = StaticFileConstants.PublicProfileCardFileName;
        string targetFolder = EnsurePath();

        string finalTargetPath = Path.Combine(targetFolder, filename);
        await _driveFileReaderWriter.WriteStringAsync(finalTargetPath, json);

        var config = new StaticFileConfiguration()
        {
            LastModified = UnixTimeUtc.Now(),
            ContentType = MediaTypeNames.Application.Json,
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        config.ContentType = MediaTypeNames.Application.Json;
        await _staticFileConfigStorage.UpsertAsync(_tableKeyValue, GetConfigKey(filename), config);
    }

    private GuidId GetConfigKey(string filename)
    {
        return new GuidId(ByteArrayUtil.ReduceSHA256Hash(filename.ToLower()));
    }

    public async Task<(StaticFileConfiguration config, bool fileExists, Stream fileStream)> GetStaticFileStreamAsync(string filename,
        UnixTimeUtc? ifModifiedSince = null)
    {
        
        var config = await _staticFileConfigStorage.GetAsync<StaticFileConfiguration>(_tableKeyValue, GetConfigKey(filename));
        var targetFile = Path.Combine(_tenantContext.TenantPathManager.StaticPath, filename);

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

        var fileStream = _driveFileReaderWriter.OpenStreamForReading(targetFile);
        return (config, fileExists: true, fileStream);
    }

    private string EnsurePath()
    {
        string targetFolder = _tenantContext.TenantPathManager.StaticPath;
        _driveFileReaderWriter.CreateDirectory(targetFolder);
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