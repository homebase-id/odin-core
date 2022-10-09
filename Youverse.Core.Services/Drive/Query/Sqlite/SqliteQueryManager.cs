using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Storage;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Storage.SQLite;

namespace Youverse.Core.Services.Drive.Query.Sqlite;

public class SqliteQueryManager : IDriveQueryManager
{
    private readonly ILogger<object> _logger;

    private readonly DriveIndexDatabase _indexDb;
    // private readonly DriveIndexDatabase _secondaryIndexDb;

    public SqliteQueryManager(StorageDrive drive, ILogger<object> logger)
    {
        Drive = drive;
        _logger = logger;

        var connectionString = $"URI=file:{drive.GetIndexPath()}\\index.db";
        _indexDb = new DriveIndexDatabase(connectionString, DatabaseIndexKind.TimeSeries);
    }

    public StorageDrive Drive { get; init; }

    public IndexReadyState IndexReadyState { get; set; }

    public Task<(ulong, IEnumerable<Guid>)> GetModified(CallerContext callerContext, FileQueryParams qp, QueryModifiedResultOptions options)
    {
        Guard.Argument(callerContext, nameof(callerContext)).NotNull();
        qp.AssertIsValid();

        var requiredSecurityGroup = new IntRange(0, (int)callerContext.SecurityLevel);
        var aclList = GetAcl(callerContext);
        var cursor = options.Cursor;

        var results = _indexDb.QueryModified(
            noOfItems: options.MaxRecords,
            cursor: ref cursor,
            stopAtModifiedUnixTimeSeconds: options.MaxDate,
            requiredSecurityGroup: requiredSecurityGroup,
            filetypesAnyOf: qp.FileType?.ToList(),
            datatypesAnyOf: qp.DataType?.ToList(),
            senderidAnyOf: qp.Sender?.ToList(),
            groupIdAnyOf: qp.GroupId?.Select(g => g.ToByteArray()).ToList(),
            userdateSpan: qp.UserDate,
            aclAnyOf: aclList,
            tagsAnyOf: qp.TagsMatchAtLeastOne?.Select(t => t.ToByteArray()).ToList(),
            tagsAllOf: qp.TagsMatchAll?.Select(t => t.ToByteArray()).ToList());

        return Task.FromResult((cursor, results.Select(r => r.FileId)));
    }


    public Task<(QueryBatchCursor, IEnumerable<Guid>)> GetBatch(CallerContext callerContext, FileQueryParams qp, QueryBatchResultOptions options)
    {
        Guard.Argument(callerContext, nameof(callerContext)).NotNull();
        qp.AssertIsValid();

        var securityRange = new IntRange(0, (int)callerContext.SecurityLevel);

        var aclList = GetAcl(callerContext);

        var cursor = options.Cursor;
        var results = _indexDb.QueryBatch(
            noOfItems: options.MaxRecords,
            cursor: ref cursor,
            requiredSecurityGroup: securityRange,
            filetypesAnyOf: qp.FileType?.ToList(),
            datatypesAnyOf: qp.DataType?.ToList(),
            senderidAnyOf: qp.Sender?.ToList(),
            groupIdAnyOf: qp.GroupId?.Select(g => g.ToByteArray()).ToList(),
            userdateSpan: qp.UserDate,
            aclAnyOf: aclList,
            tagsAnyOf: qp.TagsMatchAtLeastOne?.Select(t => t.ToByteArray()).ToList() ?? null,
            tagsAllOf: qp.TagsMatchAll?.Select(t => t.ToByteArray()).ToList());

        return Task.FromResult((cursor, results.Select(r => r.FileId)));
    }


    private List<byte[]> GetAcl(CallerContext callerContext)
    {
        var aclList = new List<byte[]>();
        if (callerContext.IsOwner == false)
        {
            if (!callerContext.IsAnonymous)
            {
                aclList.Add(callerContext.DotYouId.ToGuidIdentifier().ToByteArray());
            }

            aclList.AddRange(callerContext.Circles?.Select(c => c.Value.ToByteArray()) ?? Array.Empty<byte[]>());
        }

        return aclList.Any() ? aclList : null;
    }

    public Task SwitchIndex()
    {
        throw new NotImplementedException();
    }

    public Task UpdateCurrentIndex(ServerFileHeader header)
    {
        var metadata = header.FileMetadata;

        int securityGroup = (int)header.ServerMetadata.AccessControlList.RequiredSecurityGroup;
        var exists = _indexDb.TblMainIndex.Get(metadata.File.FileId) != null;
        
        if (header.ServerMetadata.DoNotIndex)
        {
            if (exists) // clean up if the flag was changed after it was indexed
            {
                _indexDb.TblMainIndex.DeleteRow(metadata.File.FileId);
            }
            
            return Task.CompletedTask;
        }
        
        var sender = string.IsNullOrEmpty(metadata.SenderDotYouId) ? Array.Empty<byte>() : ((DotYouIdentity)metadata.SenderDotYouId).ToByteArray();
        var acl = new List<byte[]>();

        acl.AddRange(header.ServerMetadata.AccessControlList.GetRequiredCircles().Select(c => c.ToByteArray()));
        var ids = header.ServerMetadata.AccessControlList.GetRequiredIdentities().Select(dotYouId =>
            ((DotYouIdentity)dotYouId).ToGuidIdentifier().ToByteArray()
        );
        acl.AddRange(ids.ToList());

        var tags = metadata.AppData.Tags?.Select(t => t.ToByteArray()).ToList();

        //TODO: index metadata.AppData.ClientUniqueId
        if (exists)
        {
            _indexDb.UpdateEntryZapZap(
                metadata.File.FileId,
                metadata.AppData.FileType,
                metadata.AppData.DataType,
                sender,
                metadata.AppData.GroupId.ToByteArray(),
                metadata.AppData.UserDate,
                securityGroup,
                acl,
                tags);
        }
        else
        {
            //TODO: index metadata.AppData.ClientUniqueId

            _indexDb.AddEntry(
                fileId: metadata.File.FileId,
                globalCrossReferenceId: metadata.GlobalTransitId.GetValueOrDefault(),
                metadata.AppData.FileType,
                metadata.AppData.DataType,
                sender,
                metadata.AppData.GroupId.ToByteArray(),
                metadata.AppData.UserDate.GetValueOrDefault(),
                securityGroup,
                acl,
                tags);
        }

        return Task.CompletedTask;
    }

    public Task RemoveFromCurrentIndex(InternalDriveFileId file)
    {
        _indexDb.DeleteEntry(file.FileId);
        return Task.CompletedTask;
    }

    public Task RemoveFromSecondaryIndex(InternalDriveFileId file)
    {
        throw new NotImplementedException("need a delete entry on DriveIndexDatabase");
    }

    public Task UpdateSecondaryIndex(ServerFileHeader metadata)
    {
        throw new NotImplementedException();
    }

    public Task PrepareSecondaryIndexForRebuild()
    {
        throw new NotImplementedException("Rebuild not yet supported");
    }

    public Task LoadLatestIndex()
    {
        _indexDb.CreateDatabase(false);
        this.IndexReadyState = IndexReadyState.Ready;
        return Task.CompletedTask;
    }
}