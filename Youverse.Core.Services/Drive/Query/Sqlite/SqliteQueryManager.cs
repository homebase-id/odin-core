using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive.Storage;

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

    public Task<(byte[], IEnumerable<Guid>)> GetRecent(UInt64 maxDate, byte[] startCursor, QueryParams qp, ResultOptions options)
    {
        var results = _indexDb.QueryModified(
            options.MaxRecords,
            out var cursor,
            maxDate,
            startCursor,
            qp.FileType?.ToList(),
            qp.DataType?.ToList(),
            qp.Sender?.ToList(),
            qp.ThreadId?.ToList(),
            qp.UserDateSpan,
            qp.AclId?.ToList(),
            qp.TagsMatchAtLeastOne?.ToList(),
            qp.TagsMatchAll?.ToList());

        return Task.FromResult((cursor, results.Select(r => new Guid(r))));
    }

    public Task<(byte[], byte[], UInt64, IEnumerable<Guid>)> GetBatch(byte[] startCursor, byte[] stopCursor, QueryParams qp, ResultOptions options)
    {
        var results = _indexDb.QueryBatch(
            options.MaxRecords,
            out byte[] resultFirstCursor,
            out byte[] resultLastCursor,
            out UInt64 cursorUpdatedTimestamp,
            startCursor,
            stopCursor,
            qp.FileType?.ToList(),
            qp.DataType?.ToList(),
            qp.Sender?.ToList(),
            qp.ThreadId?.ToList(),
            qp.UserDateSpan,
            qp.AclId?.ToList(),
            qp.TagsMatchAtLeastOne?.ToList(),
            qp.TagsMatchAll?.ToList());

        return Task.FromResult((resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, results.Select(r => new Guid(r))));
    }
    
    public Task SwitchIndex()
    {
        throw new NotImplementedException();
    }

    public Task UpdateCurrentIndex(ServerFileHeader header)
    {
        var metadata = header.FileMetadata;
        
        var exists = _indexDb.tblMainIndex.Get(metadata.File.FileId) != null;
        var sender = ((DotYouIdentity)metadata.SenderDotYouId).ToGuid().ToByteArray();

        //TODO: Need to sortout the ACL: header.ServerMetadata.AccessControlList
        var acl = new List<Guid>();
        var threadId = Array.Empty<byte>();
        ulong userDate = 0;

        if (exists)
        {
            _indexDb.UpdateEntry(
                metadata.File.FileId,
                metadata.AppData.FileType,
                metadata.AppData.DataType,
                sender,
                threadId,
                userDate,
                acl,
                null,
                metadata.AppData.Tags);
        }
        else
        {
            _indexDb.AddEntry(metadata.File.FileId,
                metadata.AppData.FileType,
                metadata.AppData.DataType,
                sender,
                threadId,
                userDate,
                acl,
                metadata.AppData.Tags);
        }

        return Task.CompletedTask;
    }

    public Task RemoveFromCurrentIndex(InternalDriveFileId file)
    {
        throw new NotImplementedException("need a delete entry on DriveIndexDatabase");
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
        _indexDb.CreateDatabase();
        this.IndexReadyState = IndexReadyState.Ready;
        return Task.CompletedTask;
    }
}