using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Drive;

public class CdnPublisher
{
    private readonly IDriveService _driveService;
    private readonly IDriveQueryService _driveQueryService;
    private readonly TenantContext _tenantContext;

    public CdnPublisher(IDriveService driveService, IDriveQueryService driveQueryService, TenantContext tenantContext)
    {
        _driveService = driveService;
        _driveQueryService = driveQueryService;
        _tenantContext = tenantContext;
    }

    public async Task Publish(string filename, IEnumerable<QueryParamSection> sections)
    {
        Guard.Argument(filename, nameof(filename)).NotEmpty().NotNull().Require(Validators.IsValidFilename);
        string targetFolder = EnsurePath();
        string tempFile = Guid.NewGuid().ToString("N");
        string tempTargetPath = Path.Combine(targetFolder, tempFile);

        await using var fileStream = File.Create(tempTargetPath);

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

            foreach (var header in filteredHeaders)
            {
                byte[] payload = null;
                var thumbnails = new List<ThumbnailContent>();

                if (section.ResultOptions.IncludeAdditionalThumbnails)
                {
                    foreach (var thumbHeader in header.FileMetadata.AppData.AdditionalThumbnails)
                    {
                        var thumbnailStream = await _driveService.GetThumbnailPayloadStream(header.FileMetadata.File, thumbHeader.PixelWidth, thumbHeader.PixelHeight);
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
                    var payloadStream = await _driveService.GetPayloadStream(header.FileMetadata.File);
                    payload = payloadStream.ToByteArray();
                }

                sectionOutput.Files.Add(new StaticFile()
                {
                    Header = header,
                    AdditionalThumbnails = thumbnails,
                    Payload = payload
                });
            }

            await JsonSerializer.SerializeAsync(fileStream, sectionOutput, sectionOutput.GetType(), SerializationConfiguration.JsonSerializerOptions);
        }
    }

    private string EnsurePath()
    {
        string targetFolder = Path.Combine(_tenantContext.TempDataRoot, "static");

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

public class SectionOutput
{
    public string Name { get; set; }

    public List<StaticFile> Files { get; set; }
}

public class StaticFile
{
    public ClientFileHeader Header { get; set; }

    public IEnumerable<ThumbnailContent> AdditionalThumbnails { get; set; }

    /// <summary>
    /// Base64 encoded byte array of the payload
    /// </summary>
    public byte[] Payload { get; set; }
}

public class QueryParamSection
{
    public string Name { get; set; }

    public FileQueryParams QueryParams { get; set; }

    public SectionResultOptions ResultOptions { get; set; }
    //todo: need to be able to exclude/include payload
}

public class SectionResultOptions
{
    /// <summary>
    /// If true, the content of the additional thumbnails defined in the metadata will be included in each file.
    /// </summary>
    public bool IncludeAdditionalThumbnails { get; set; }

    /// <summary>
    /// If true, the metadata.JsonContent field will be included in each file
    /// </summary>
    public bool IncludeJsonContent { get; set; }

    /// <summary>
    /// If true, the payload of each file will be included.
    /// </summary>
    public bool IncludePayload { get; set; }

    /// <summary>
    /// If true, the preview thumbnail will not be included in the file
    /// </summary>
    public bool ExcludePreviewThumbnail { get; set; }
}