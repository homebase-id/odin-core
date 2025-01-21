using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Abstractions
{
    public class TransferHistoryDataOperations(
        ScopedIdentityConnectionFactory scopedConnectionFactory,
        IdentityKey identityKey,
        TableDriveTransferHistory driveTransferHistory,
        TableDriveMainIndex driveMainIndex)
    {
        /// <summary>
        /// Upserts transfer history records
        /// </summary>
        public async Task<int> UpsertTransferHistoryRecordAsync(Guid driveId, Guid fileId, OdinId recipient,
            int? latestTransferStatus,
            Guid? latestSuccessfullyDeliveredVersionTag,
            bool? isInOutbox,
            bool? isReadByRecipient)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            await using var upsertCommand = cn.CreateCommand();

            upsertCommand.CommandText =
                "INSERT INTO driveTransferHistory (identityId, driveId, fileId, remoteIdentityId, isInOutbox, latestSuccessfullyDeliveredVersionTag, isReadByRecipient, latestTransferStatus) " +
                "VALUES (@identityId, @driveId, @fileId, @remoteIdentityId, @isInOutbox, @latestSuccessfullyDeliveredVersionTag, @isReadByRecipient, @latestTransferStatus) " +
                "ON CONFLICT (identityId, driveId, fileId, remoteIdentityId) " +
                "DO UPDATE " +
                "SET isInOutbox = COALESCE(@isInOutbox, driveTransferHistory.isInOutbox), " +
                "    latestSuccessfullyDeliveredVersionTag = COALESCE(@latestSuccessfullyDeliveredVersionTag, driveTransferHistory.latestSuccessfullyDeliveredVersionTag), " +
                "    isReadByRecipient = COALESCE(@isReadByRecipient, driveTransferHistory.isReadByRecipient), " +
                "    latestTransferStatus = COALESCE(@latestTransferStatus, driveTransferHistory.latestTransferStatus);";

            var upsertParam1 = upsertCommand.CreateParameter();
            upsertParam1.ParameterName = "@identityId";
            upsertCommand.Parameters.Add(upsertParam1);
            var upsertParam2 = upsertCommand.CreateParameter();
            upsertParam2.ParameterName = "@driveId";
            upsertCommand.Parameters.Add(upsertParam2);
            var upsertParam3 = upsertCommand.CreateParameter();
            upsertParam3.ParameterName = "@fileId";
            upsertCommand.Parameters.Add(upsertParam3);
            var upsertParam4 = upsertCommand.CreateParameter();
            upsertParam4.ParameterName = "@remoteIdentityId";
            upsertCommand.Parameters.Add(upsertParam4);
            var upsertParam5 = upsertCommand.CreateParameter();
            upsertParam5.ParameterName = "@isInOutbox";
            upsertCommand.Parameters.Add(upsertParam5);
            var upsertParam6 = upsertCommand.CreateParameter();
            upsertParam6.ParameterName = "@latestSuccessfullyDeliveredVersionTag";
            upsertCommand.Parameters.Add(upsertParam6);
            var upsertParam7 = upsertCommand.CreateParameter();
            upsertParam7.ParameterName = "@isReadByRecipient";
            upsertCommand.Parameters.Add(upsertParam7);

            var upsertParam8 = upsertCommand.CreateParameter();
            upsertParam8.ParameterName = "@latestTransferStatus";
            upsertCommand.Parameters.Add(upsertParam8);

            upsertParam1.Value = identityKey.ToByteArray();
            upsertParam2.Value = driveId.ToByteArray();
            upsertParam3.Value = fileId.ToByteArray();
            upsertParam4.Value = recipient.DomainName;
            upsertParam5.Value = isInOutbox ?? (object)DBNull.Value;
            upsertParam6.Value = latestSuccessfullyDeliveredVersionTag?.ToByteArray() ?? (object)DBNull.Value;
            upsertParam7.Value = isReadByRecipient ?? (object)DBNull.Value;
            upsertParam8.Value = latestTransferStatus ?? (object)DBNull.Value;

            var count = await upsertCommand.ExecuteNonQueryAsync();

            tx.Commit();

            return count;
        }

        public async Task<int> DeleteTransferHistoryAsync(Guid driveId, Guid fileId)
        {
            //TODO: do we need to clear the main index field in the table?
            return await driveTransferHistory.DeleteAllRowsAsync(driveId, fileId);
        }


        public async Task<List<DriveTransferHistoryRecord>> GetTransferHistoryAsync(Guid driveId, Guid fileId)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCommand = cn.CreateCommand();

            getCommand.CommandText =
                "SELECT remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient " +
                "FROM driveTransferHistory " +
                "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;";

            var get1Param1 = getCommand.CreateParameter();
            get1Param1.ParameterName = "@identityId";
            getCommand.Parameters.Add(get1Param1);
            var get1Param2 = getCommand.CreateParameter();
            get1Param2.ParameterName = "@driveId";
            getCommand.Parameters.Add(get1Param2);
            var get1Param3 = getCommand.CreateParameter();
            get1Param3.ParameterName = "@fileId";
            getCommand.Parameters.Add(get1Param3);

            get1Param1.Value = identityKey.ToByteArray();
            get1Param2.Value = driveId.ToByteArray();
            get1Param3.Value = fileId.ToByteArray();
            {
                using (var rdr = await getCommand.ExecuteReaderAsync())
                {
                    if (await rdr.ReadAsync() == false)
                    {
                        return new List<DriveTransferHistoryRecord>();
                    }

                    var result = new List<DriveTransferHistoryRecord>();
                    while (true)
                    {
                        var item = new DriveTransferHistoryRecord
                        {
                            identityId = identityKey,
                            driveId = driveId,
                            fileId = fileId,
                            remoteIdentityId = new OdinId((string)rdr[0]),
                            latestTransferStatus = rdr.IsDBNull(1) ? 0 : (int)(long)rdr[1],
                            isInOutbox = rdr.IsDBNull(2) ? 0 : (int)(long)rdr[2],
                            latestSuccessfullyDeliveredVersionTag = rdr.IsDBNull(3) ? null : new Guid((byte[])rdr[3]),
                            isReadByRecipient = rdr.IsDBNull(4) ? 0 : (int)(long)rdr[4]
                        };
                        result.Add(item);

                        if (!await rdr.ReadAsync())
                        {
                            break;
                        }
                    }

                    return result;
                }
            }
        }

        public async Task UpdateTransferSummaryCacheAsync(Guid driveId, Guid fileId, string json)
        {
            await driveMainIndex.UpdateTransferSummaryAsync(driveId, fileId, json);
        }
    }
}