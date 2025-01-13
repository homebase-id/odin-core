using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Abstractions
{
    public class TransferHistoryDataOperations(
        ScopedIdentityConnectionFactory scopedConnectionFactory,
        IdentityKey identityKey,
        TableDriveTransferHistory driveTransferHistory)
    {
        /// <summary>
        /// Upserts transfer history records
        /// </summary>
        public async Task<int> UpsertTransferHistory(Guid driveId, Guid fileId, OdinId recipient,
            int? latestTransferStatus,
            Guid? latestSuccessfullyDeliveredVersionTag,
            bool? isInOutbox,
            bool? isReadByRecipient)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            await using var upsertCommand = cn.CreateCommand();

            upsertCommand.CommandText =
                "INSERT INTO driveTransferHistory (identityId, driveId, fileId, remoteIdentityId, isInOutbox, latestSuccessfullyDeliveredVersionTag, isReadByRecipient) " +
                "VALUES (@identityId, @driveId, @fileId, @remoteIdentityId, @isInOutbox, @latestSuccessfullyDeliveredVersionTag, @isReadByRecipient) " +
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
    }
}