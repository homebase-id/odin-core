﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Abstractions
{
    public class LocalMetadataDataOperations(
        ScopedIdentityConnectionFactory scopedConnectionFactory,
        IdentityKey identityKey,
        TableDriveLocalTagIndex driveLocalTagIndex)
    {
        /// <summary>
        /// Updates only the local tags table used when Querying the files (querybatch, query-modified, and futures)
        /// </summary>
        public async Task UpdateLocalTagsAsync(Guid driveId, Guid fileId, List<Guid> tags)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            await driveLocalTagIndex.DeleteAllRowsAsync(driveId, fileId);
            await driveLocalTagIndex.InsertRowsAsync(driveId, fileId, tags);

            tx.Commit();
        }

        public async Task<int> UpdateLocalAppMetadataAsync(Guid driveId, Guid fileId, Guid newVersionTag, string localMetadataJson)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();

            updateCommand.CommandText = $"UPDATE driveMainIndex " +
                                        $"SET hdrLocalVersionTag=@hdrLocalVersionTag,hdrLocalAppData=@hdrLocalAppData,modified=@modified " +
                                        $"WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

            var sparam1 = updateCommand.CreateParameter();
            var sparam2 = updateCommand.CreateParameter();
            var sparam3 = updateCommand.CreateParameter();
            var versionTagParam = updateCommand.CreateParameter();
            var contentParam = updateCommand.CreateParameter();
            var modifiedParam = updateCommand.CreateParameter();

            sparam1.ParameterName = "@identityId";
            sparam2.ParameterName = "@driveId";
            sparam3.ParameterName = "@fileId";
            versionTagParam.ParameterName = "@hdrLocalVersionTag";
            contentParam.ParameterName = "@hdrLocalAppData";
            modifiedParam.ParameterName = "@modified";

            updateCommand.Parameters.Add(sparam1);
            updateCommand.Parameters.Add(sparam2);
            updateCommand.Parameters.Add(sparam3);
            updateCommand.Parameters.Add(versionTagParam);
            updateCommand.Parameters.Add(contentParam);
            updateCommand.Parameters.Add(modifiedParam);

            sparam1.Value = identityKey.ToByteArray();
            sparam2.Value = driveId.ToByteArray();
            sparam3.Value = fileId.ToByteArray();
            versionTagParam.Value = newVersionTag.ToByteArray();
            contentParam.Value = localMetadataJson;
            modifiedParam.Value = UnixTimeUtcUnique.Now().uniqueTime;

            return await updateCommand.ExecuteNonQueryAsync();
        }
    }
}