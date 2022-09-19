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
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Storage;

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
    private readonly IDriveService _driveService;
    private readonly IDriveQueryService _driveQueryService;
    private readonly TenantContext _tenantContext;
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly ISystemStorage _systemStorage;

    public StaticFileContentService(IDriveService driveService, IDriveQueryService driveQueryService, TenantContext tenantContext, DotYouContextAccessor contextAccessor, ISystemStorage systemStorage)
    {
        _driveService = driveService;
        _driveQueryService = driveQueryService;
        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
        _systemStorage = systemStorage;
    }

    public async Task<StaticFilePublishResult> Publish(string filename, StaticFileConfiguration config, List<QueryParamSection> sections)
    {
        //
        //TODO: optimize we need update this method to serialize in small chunks and write to stream instead of building a huge array of everything then serialization
        //

        //Note: I need to add a permission that bettter describes that we only wnt this done when the owner is in full
        //admin mode, not just from an app.  master key idicates you're in full admin mode
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
            var driveId = await _driveService.GetDriveIdByAlias(qp.TargetDrive, true);

            var options = new QueryBatchResultOptions()
            {
                IncludeJsonContent = section.ResultOptions.IncludeJsonContent,
                ExcludePreviewThumbnail = section.ResultOptions.ExcludePreviewThumbnail,
                Cursor = null, //TODO?
                MaxRecords = int.MaxValue //TODO: Consider
            };

            var results = await _driveQueryService.GetBatch(driveId.GetValueOrDefault(), qp, options);
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

                if (section.ResultOptions.IncludeAdditionalThumbnails)
                {
                    foreach (var thumbHeader in fileHeader.FileMetadata.AppData.AdditionalThumbnails)
                    {
                        var thumbnailStream = await _driveService.GetThumbnailPayloadStream(fileHeader.FileMetadata.File, thumbHeader.PixelWidth, thumbHeader.PixelHeight);
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
                    var payloadStream = await _driveService.GetPayloadStream(fileHeader.FileMetadata.File);
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

            await using var fileStream = File.Create(tempTargetPath);

            await DotYouSystemSerializer.Serialize(fileStream, sectionOutputList, sectionOutputList.GetType());

            string finalTargetPath = Path.Combine(targetFolder, filename);
            File.Move(tempTargetPath, finalTargetPath, true);
        }

        config.ContentType = MediaTypeNames.Application.Json;
        _systemStorage.SingleKeyValueStorage.Upsert(GetConfigKey(filename), config);

        return result;
    }

    public Task<(StaticFileConfiguration config, Stream fileStream)> GetStaticFileStream(string filename)
    {
        Guard.Argument(filename, nameof(filename)).NotEmpty().NotNull().Require(Validators.IsValidFilename);
        string targetFile = Path.Combine(_tenantContext.StaticFileDataRoot, filename);

        var config = _systemStorage.SingleKeyValueStorage.Get<StaticFileConfiguration>(GetConfigKey(filename));

        if (!File.Exists(targetFile))
        {
            return Task.FromResult((config, (Stream)null));
        }

        var fileStream = File.Open(targetFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult((config, (Stream)fileStream));
    }

    private ByteArrayId GetConfigKey(string filename)
    {
        return new ByteArrayId(filename.ToLower().ToUtf8ByteArray());
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
        return headers.Where(r => r.FileMetadata.PayloadIsEncrypted == false && r.ServerMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Anonymous);
    }
}