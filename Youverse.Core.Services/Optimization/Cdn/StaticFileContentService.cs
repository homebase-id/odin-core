using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace Youverse.Core.Services.Optimization.Cdn;

public class StaticFileContentService
{
    private readonly IDriveService _driveService;
    private readonly IDriveQueryService _driveQueryService;
    private readonly TenantContext _tenantContext;
    private readonly DotYouContextAccessor _contextAccessor;

    public StaticFileContentService(IDriveService driveService, IDriveQueryService driveQueryService, TenantContext tenantContext, DotYouContextAccessor contextAccessor)
    {
        _driveService = driveService;
        _driveQueryService = driveQueryService;
        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
    }

    public async Task<StaticFilePublishResult> Publish(string filename, List<QueryParamSection> sections)
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
                var thumbnails = new List<ThumbnailContent>();

                if (section.ResultOptions.IncludeAdditionalThumbnails)
                {
                    foreach (var thumbHeader in fileHeader.FileMetadata.AppData.AdditionalThumbnails)
                    {
                        var thumbnailStream = await _driveService.GetThumbnailPayloadStream(fileHeader.FileMetadata.File, thumbHeader.PixelWidth, thumbHeader.PixelHeight);
                        thumbnails.Add(new ThumbnailContent()
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
            await JsonSerializer.SerializeAsync(fileStream, sectionOutputList, sectionOutputList.GetType(), SerializationConfiguration.JsonSerializerOptions);
            
            string finalTargetPath = Path.Combine(targetFolder, filename);
            File.Move(tempTargetPath, finalTargetPath, true);
        }

        return result;
    }

    public Task<Stream> GetStaticFileStream(string filename)
    {
        Guard.Argument(filename, nameof(filename)).NotEmpty().NotNull().Require(Validators.IsValidFilename);
        string targetFile = Path.Combine(_tenantContext.StaticFileDataRoot, filename);

        if (!File.Exists(targetFile))
        {
            return null;
        }

        var fileStream = File.Open(targetFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult((Stream)fileStream);
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