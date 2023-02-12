using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Storage;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Optimization.Cdn;

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
    private readonly IDriveStorageService _driveStorageService;
    private readonly IDriveQueryService _driveQueryService;
    private readonly TenantContext _tenantContext;
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly ITenantSystemStorage _tenantSystemStorage;

    public StaticFileContentService(IDriveStorageService driveStorageService, IDriveQueryService driveQueryService,
        TenantContext tenantContext, DotYouContextAccessor contextAccessor, ITenantSystemStorage tenantSystemStorage, DriveManager driveManager)
    {
        _driveStorageService = driveStorageService;
        _driveQueryService = driveQueryService;
        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
        _tenantSystemStorage = tenantSystemStorage;
        _driveManager = driveManager;
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
                IncludeJsonContent = section.ResultOptions.IncludeJsonContent,
                ExcludePreviewThumbnail = section.ResultOptions.ExcludePreviewThumbnail,
                Cursor = null, //TODO?
                MaxRecords = int.MaxValue //TODO: Consider
            };

            var results = await _driveQueryService.GetBatch(driveId, qp, options);
            var filteredHeaders = Filter(results.SearchResults);

            var sectionOutput = new SectionOutput()
            {
                Name = section.Name,
                Files = new List<StaticFile>()
            };
            sectionOutputList.Add(sectionOutput);

            foreach (var fileHeader in filteredHeaders)
            {
                byte[] payload = null;
                var thumbnails = new List<ImageDataContent>();
                var internalFileId = new InternalDriveFileId()
                {
                    FileId = fileHeader.FileId,
                    DriveId = driveId
                };

                if (section.ResultOptions.IncludeAdditionalThumbnails)
                {
                    foreach (var thumbHeader in fileHeader.FileMetadata.AppData?.AdditionalThumbnails ??
                                                new List<ImageDataHeader>())
                    {
                        var thumbnailStream = await _driveStorageService.GetThumbnailPayloadStream(
                            internalFileId, thumbHeader.PixelWidth, thumbHeader.PixelHeight);

                        thumbnails.Add(new ImageDataContent()
                        {
                            PixelHeight = thumbHeader.PixelHeight,
                            PixelWidth = thumbHeader.PixelWidth,
                            ContentType = thumbHeader.ContentType,
                            Content = thumbnailStream.ToByteArray()
                        });
                    }
                }

                if (section.ResultOptions.IncludePayload)
                {
                    var payloadStream = await _driveStorageService.GetPayloadStream(internalFileId);
                    payload = payloadStream.ToByteArray();
                }

                sectionOutput.Files.Add(new StaticFile()
                {
                    Header = fileHeader,
                    AdditionalThumbnails = thumbnails,
                    Payload = payload
                });
            }

            result.SectionResults.Add(new SectionPublishResult()
            {
                Name = sectionOutput.Name,
                FileCount = sectionOutput.Files.Count
            });
        }

        await using var fileStream = File.Create(tempTargetPath);
        await DotYouSystemSerializer.Serialize(fileStream, sectionOutputList, sectionOutputList.GetType());
        fileStream.Close();

        string finalTargetPath = Path.Combine(targetFolder, filename);

        File.Move(tempTargetPath, finalTargetPath, true);
        config.ContentType = MediaTypeNames.Application.Json;
        _tenantSystemStorage.SingleKeyValueStorage.Upsert(GetConfigKey(filename), config);

        return result;
    }

    public Task<(StaticFileConfiguration config, Stream fileStream)> GetStaticFileStream(string filename)
    {
        Guard.Argument(filename, nameof(filename)).NotEmpty().NotNull().Require(Validators.IsValidFilename);
        string targetFile = Path.Combine(_tenantContext.StaticFileDataRoot, filename);

        var config = _tenantSystemStorage.SingleKeyValueStorage.Get<StaticFileConfiguration>(GetConfigKey(filename));


        var fileStream = File.Open(targetFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult((config, (Stream)fileStream));
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
        File.Move(tempTargetPath, finalTargetPath, true);

        var config = new StaticFileConfiguration()
        {
            ContentType = contentType,
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        _tenantSystemStorage.SingleKeyValueStorage.Upsert(GetConfigKey(filename), config);
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
        File.Move(tempTargetPath, finalTargetPath, true);

        var config = new StaticFileConfiguration()
        {
            ContentType = MediaTypeNames.Application.Json,
            CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
        };

        config.ContentType = MediaTypeNames.Application.Json;
        _tenantSystemStorage.SingleKeyValueStorage.Upsert(GetConfigKey(filename), config);
    }

    private GuidId GetConfigKey(string filename)
    {
        return new GuidId(HashUtil.ReduceSHA256Hash(filename.ToLower()));
    }

    private string EnsurePath()
    {
        string targetFolder = _tenantContext.StaticFileDataRoot;

        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        return targetFolder;
    }

    private IEnumerable<ClientFileHeader> Filter(IEnumerable<ClientFileHeader> headers)
    {
        return headers.Where(r =>
            r.FileState == FileState.Active &&
            r.FileMetadata.PayloadIsEncrypted == false &&
            r.ServerMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Anonymous);
    }
}