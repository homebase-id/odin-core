using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveTransferHistory(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDriveTransferHistoryCRUD(scopedConnectionFactory: scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<int> UpdateTransferHistoryRecordAsync(Guid driveId, Guid fileId, OdinId recipient,
        int? latestTransferStatus,
        Guid? latestSuccessfullyDeliveredVersionTag,
        bool? isInOutbox,
        bool? isReadByRecipient)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        await using var updateCommand = cn.CreateCommand();

        // Start building the SQL query
        var sql = new StringBuilder(@"UPDATE driveTransferHistory SET ");

        // Dynamic UPDATE statement
        var updateFields = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (isInOutbox.HasValue)
        {
            updateFields.Add("isInOutbox = @isInOutbox");
            parameters["@isInOutbox"] = isInOutbox.Value;
        }

        if (latestSuccessfullyDeliveredVersionTag.HasValue)
        {
            updateFields.Add("latestSuccessfullyDeliveredVersionTag = @latestSuccessfullyDeliveredVersionTag");
            parameters["@latestSuccessfullyDeliveredVersionTag"] = latestSuccessfullyDeliveredVersionTag.Value.ToByteArray();
        }

        if (isReadByRecipient.HasValue)
        {
            updateFields.Add("isReadByRecipient = @isReadByRecipient");
            parameters["@isReadByRecipient"] = isReadByRecipient.Value;
        }

        if (latestTransferStatus.HasValue)
        {
            updateFields.Add("latestTransferStatus = @latestTransferStatus");
            parameters["@latestTransferStatus"] = latestTransferStatus.Value;
        }

        // Ensure there is at least one field to update
        if (updateFields.Count == 0)
        {
            throw new InvalidOperationException("No updatable fields were provided.");
        }

        sql.Append(string.Join(", ", updateFields));

        // Add WHERE condition to target specific record
        sql.Append(@"
        WHERE identityId = @identityId 
          AND driveId = @driveId 
          AND fileId = @fileId 
          AND remoteIdentityId = @remoteIdentityId");

        updateCommand.CommandText = sql.ToString();

        // Add required WHERE clause parameters
        parameters["@identityId"] = odinIdentity.IdentityIdAsByteArray();
        parameters["@driveId"] = driveId.ToByteArray();
        parameters["@fileId"] = fileId.ToByteArray();
        parameters["@remoteIdentityId"] = recipient.DomainName;

        // Add parameters to the command
        foreach (var param in parameters)
        {
            var sqlParam = updateCommand.CreateParameter();
            sqlParam.ParameterName = param.Key;
            sqlParam.Value = param.Value;
            updateCommand.Parameters.Add(sqlParam);
        }

        var count = await updateCommand.ExecuteNonQueryAsync();
        tx.Commit();

        //
        // WE SHOULD PROBABLY TOUCH THE MAINDRIVE RECORD HERE
        // I'LL IMPLEMENT A GENERATOR TOUCH FUNCTION FOR ALL TABLES IF YOU AGREE.
        //

        return count;
    }


    public async Task<List<DriveTransferHistoryRecord>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId);
    }


    public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(odinIdentity, driveId, fileId);
    }

    public new async Task<int> InsertAsync(DriveTransferHistoryRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public Task<bool> TryAddInitialRecordAsync(Guid driveId, Guid fileId, OdinId recipient)
    {
        var item = new DriveTransferHistoryRecord
        {
            identityId = odinIdentity,
            driveId = driveId,
            fileId = fileId,
            remoteIdentityId = recipient,
            latestTransferStatus = 0,
            isInOutbox = true,
            latestSuccessfullyDeliveredVersionTag = null,
            isReadByRecipient = false
        };

        return base.TryInsertAsync(item);
    }
}