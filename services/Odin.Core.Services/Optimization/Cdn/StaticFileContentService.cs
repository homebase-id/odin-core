using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Cryptography;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Standard;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Optimization.Cdn;

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
    private readonly OdinContextAccessor _contextAccessor;
    private readonly SingleKeyValueStorage _staticFileConfigStorage;
    private readonly OdinConfiguration _odinConfiguration;

    public StaticFileContentService(TenantContext tenantContext, OdinContextAccessor contextAccessor, TenantSystemStorage tenantSystemStorage,
        DriveManager driveManager, StandardFileSystem fileSystem, OdinConfiguration odinConfiguration)
    {
        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
        _driveManager = driveManager;
        _fileSystem = fileSystem;
        _odinConfiguration = odinConfiguration;

        const string staticFileContextKey = "3609449a-2f7f-4111-b300-3408a920aa2e";
        _staticFileConfigStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(staticFileContextKey));
    }

    public async Task<StaticFilePublishResult> Publish(string filename, StaticFileConfiguration config,
        List<QueryParamSection> sections)
    {
        //
        //TODO: optimize we need update this method to serialize in small chunks and write to stream instead of building a huge array of everything then serialization
        //

        //Note: I need to add a permission that better describes that we only wnt this done when the owner is in full
        //admin mode, not just from an app.  master key indicates you're in full admin mode
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        Guard.Argument(filename, nameof(filename)).NotEmpty().NotNull().Require(Validators.IsValidFilename);
        Guard.Argument(sections, nameof(sections)).NotNull().NotEmpty();
        string targetFolder = EnsurePath();
        string tempFile = Guid.NewGuid().ToString("N");
        string tempTargetPath = Path.Combine(targetFolder, tempFile);
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
            var driveId = (await _driveManager.GetDriveIdByAlias(qp.TargetDrive, true)).GetValueOrDefault();

            var options = new QueryBatchResultOptions()
            {
                IncludeHeaderContent = section.ResultOptions.IncludeHeaderContent,
                ExcludePreviewThumbnail = section.ResultOptions.ExcludePreviewThumbnail,
                Cursor = null, //TODO?
                MaxRecords = int.MaxValue //TODO: Consider
            };

            var results = await _fileSystem.Query.GetBatch(driveId, qp, options);
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

                        var ps = await _fileSystem.Storage.GetPayloadStream(internalFileId, pd.Key, null);
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

        await using var fileStream = File.Create(tempTargetPath);
        await OdinSystemSerializer.Serialize(fileStream, sectionOutputList, sectionOutputList.GetType());
        fileStream.Close();

        string finalTargetPath = Path.Combine(targetFolder, filename);

        // File.Move(tempTargetPath, finalTargetPath, true);
        IoUtils.RetryOperation(() => File.Move(tempTargetPath, finalTargetPath, true),
            _odinConfiguration.Host.FileMoveRetryAttempts,
            _odinConfiguration.Host.FileMoveRetryDelayMs, 
            $"Publish source ({tempTargetPath}) to {finalTargetPath}");
        
        config.ContentType = MediaTypeNames.Application.Json;
        config.LastModified = UnixTimeUtc.Now();

        _staticFileConfigStorage.Upsert(GetConfigKey(filename), config);

        return result;
    }

    public async Task PublishProfileImage(string image64, string contentType)
    {
        string filename = StaticFileConstants.ProfileImageFileName;
        string targetFolder = EnsurePath();
        string tempTargetPath = Path.Combine(targetFolder, Guid.NewGuid().ToString("N"));

        await using var fileStream = File.Create(tempTargetPath);
        var imageBytes = Convert.FromBase64String(image64);
        await fileStream.WriteAsync(imageBytes);
        fileStream.Close();

        string finalTargetPath = Path.Combine(targetFolder, filename);
        // File.Move(tempTargetPath, finalTargetPath, true);
        IoUtils.RetryOperation(() => File.Move(tempTargetPath, finalTargetPath, true), 
            _odinConfiguration.Host.FileMoveRetryAttempts, 
            _odinConfiguration.Host.FileMoveRetryDelayMs, 
            $"PublishProfileImage source ({tempTargetPath}) to {finalTargetPath}");

        var config = new StaticFileConfiguration()
        {
            ContentType = contentType,
            LastModified = UnixTimeUtc.Now(),
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        _staticFileConfigStorage.Upsert(GetConfigKey(filename), config);
    }

    public async Task PublishProfileCard(string json)
    {
        string filename = StaticFileConstants.PublicProfileCardFileName;
        string targetFolder = EnsurePath();
        string tempTargetPath = Path.Combine(targetFolder, Guid.NewGuid().ToString("N"));

        await using var fileStream = new StreamWriter(File.Create(tempTargetPath));
        await fileStream.WriteAsync(json);
        fileStream.Close();

        string finalTargetPath = Path.Combine(targetFolder, filename);
        // File.Move(tempTargetPath, finalTargetPath, true);
        IoUtils.RetryOperation(() => File.Move(tempTargetPath, finalTargetPath, true),
            _odinConfiguration.Host.FileMoveRetryAttempts,
            _odinConfiguration.Host.FileMoveRetryDelayMs, 
            $"PublishProfileCard source ({tempTargetPath}) to {finalTargetPath}");

        var config = new StaticFileConfiguration()
        {
            LastModified = UnixTimeUtc.Now(),
            ContentType = MediaTypeNames.Application.Json,
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        config.ContentType = MediaTypeNames.Application.Json;
        _staticFileConfigStorage.Upsert(GetConfigKey(filename), config);
    }

    private GuidId GetConfigKey(string filename)
    {
        return new GuidId(ByteArrayUtil.ReduceSHA256Hash(filename.ToLower()));
    }

    public Task<(StaticFileConfiguration config, bool fileExists, Stream fileStream)> GetStaticFileStream(string filename, UnixTimeUtc? ifModifiedSince = null)
    {
        Guard.Argument(filename, nameof(filename)).NotEmpty().NotNull().Require(Validators.IsValidFilename);

        var config = _staticFileConfigStorage.Get<StaticFileConfiguration>(GetConfigKey(filename));
        var targetFile = Path.Combine(_tenantContext.StorageConfig.StaticFileStoragePath, filename);

        if (config == null || !File.Exists(targetFile))
        {
            return Task.FromResult(((StaticFileConfiguration)null, false, Stream.Null));
        }

        if (ifModifiedSince != null) //I was asked to check...
        {
            bool wasModified = config.LastModified > ifModifiedSince;
            if (!wasModified)
            {
                return Task.FromResult((config, fileExists: true, Stream.Null));
            }
        }

        var fileStream = File.Open(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Task.FromResult((config, fileExists: true, (Stream)fileStream));
    }

    private string EnsurePath()
    {
        string targetFolder = _tenantContext.StorageConfig.StaticFileStoragePath;

        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

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