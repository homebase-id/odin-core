using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;
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

    public Task<(byte[], IEnumerable<Guid>)> GetRecent(CallerContext callerContext, UInt64 maxDate, byte[] startCursor, QueryParams qp, ResultOptions options)
    {
        Guard.Argument(callerContext, nameof(callerContext)).NotNull();

        if (callerContext.IsOwner)
        {
            //query without enforcing security
        }

        var aclList = new List<byte[]>();

        //TODO: add required security group to the querymodified function
        var results = _indexDb.QueryModified(
            options.MaxRecords,
            out var cursor,
            maxDate,
            startCursor,
            qp.FileType?.ToList(),
            qp.DataType?.ToList(),
            qp.Sender?.ToList(),
            null,
            qp.UserDate,
            null,
            qp.TagsMatchAtLeastOne?.ToList(),
            qp.TagsMatchAll?.ToList());

        return Task.FromResult((cursor, results.Select(r => new Guid(r))));
    }

    public Task<(byte[], byte[], ulong, IEnumerable<Guid>)> GetBatch(CallerContext callerContext, byte[] startCursor, byte[] stopCursor, QueryParams qp, ResultOptions options)
    {
        Guard.Argument(callerContext, nameof(callerContext)).NotNull();

        var requiredSecurityGroup = 1;
        if (callerContext.IsAnonymous)
        {
            requiredSecurityGroup = (int)SecurityGroupType.Anonymous;
        }
        
        if (callerContext.IsInYouverseNetwork)
        {
            requiredSecurityGroup = (int)SecurityGroupType.Authenticated;
        }
        
        if (callerContext.IsConnected)
        {
            requiredSecurityGroup = (int)SecurityGroupType.Connected;
        }

        if (callerContext.IsOwner)
        {
            requiredSecurityGroup = (int)SecurityGroupType.Owner;
        }
        
        // todo: how to handle these? 
        // (int)SecurityGroupType.CircleConnected 
        // (int)SecurityGroupType.CustomList

            
        var aclList = new List<byte[]>();

        //if the caller is not owner
        // we have to pass the data thru a filter where the only files returned are those which match
        //TODO: add required security group to the query modified function

        var results = _indexDb.QueryBatch(
            options.MaxRecords,
            out byte[] resultFirstCursor,
            out byte[] resultLastCursor,
            out UInt64 cursorUpdatedTimestamp,
            startCursor,
            stopCursor,
            requiredSecurityGroup, 
            qp.FileType?.ToList(),
            qp.DataType?.ToList(),
            qp.Sender?.ToList(),
            null, //thread id list
            qp.UserDate,
            null, //acl list  
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

        int securityGroup = (int)header.ServerMetadata.AccessControlList.RequiredSecurityGroup;
        var exists = _indexDb.TblMainIndex.Get(metadata.File.FileId) != null;
        var sender = ((DotYouIdentity)metadata.SenderDotYouId).ToGuid().ToByteArray();

        var acl = new List<Guid>();
        acl.AddRange(header.ServerMetadata.AccessControlList.GetRequiredCircles());
        
        //TODO: look up identities
        if (header.ServerMetadata.AccessControlList.GetRequiredIdentities().Any())
        {
            throw new NotImplementedException("need to map the identity to its Id");
            // acl.AddRange(identityGuidList);
        }

        var threadId = Array.Empty<byte>();

        if (exists)
        {
            _indexDb.UpdateEntryZapZap(
                metadata.File.FileId,
                metadata.AppData.FileType,
                metadata.AppData.DataType,
                sender,
                threadId,
                metadata.AppData.UserDate,
                securityGroup,
                acl,
                metadata.AppData.Tags);
        }
        else
        {
            _indexDb.AddEntry(metadata.File.FileId,
                metadata.AppData.FileType,
                metadata.AppData.DataType,
                sender,
                threadId,
                metadata.AppData.UserDate,
                securityGroup,
                acl,
                metadata.AppData.Tags);
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