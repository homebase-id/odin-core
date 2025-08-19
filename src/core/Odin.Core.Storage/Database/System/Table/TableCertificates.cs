using System;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.System.Table;

#nullable enable

public class TableCertificates(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableCertificatesCRUD(cache, scopedConnectionFactory)
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    //

    public async Task<int> FailCertificateUpdate(OdinId domain, UnixTimeUtc lastAttempt, string correlationId, string lastError)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var upsertCommand = cn.CreateCommand();
        {
            string sqlNowStr = upsertCommand.SqlNow();
            upsertCommand.CommandText = "INSERT INTO Certificates (domain,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified) " +
                                        $"VALUES (@domain,'','',0,@lastAttempt,@correlationId,@lastError,{sqlNowStr},{sqlNowStr})"+
                                        "ON CONFLICT (domain) DO UPDATE "+
                                        $"SET lastAttempt = @lastAttempt,correlationId = @correlationId,lastError = @lastError,modified = {upsertCommand.SqlMax()}(Certificates.modified+1,{sqlNowStr}) "+
                                        "RETURNING created,modified,rowId;";

            var upsertParam1 = upsertCommand.CreateParameter();
            upsertParam1.DbType = DbType.String;
            upsertParam1.ParameterName = "@domain";
            upsertCommand.Parameters.Add(upsertParam1);
            var upsertParam5 = upsertCommand.CreateParameter();
            upsertParam5.DbType = DbType.Int64;
            upsertParam5.ParameterName = "@lastAttempt";
            upsertCommand.Parameters.Add(upsertParam5);
            var upsertParam6 = upsertCommand.CreateParameter();
            upsertParam6.DbType = DbType.String;
            upsertParam6.ParameterName = "@correlationId";
            upsertCommand.Parameters.Add(upsertParam6);
            var upsertParam7 = upsertCommand.CreateParameter();
            upsertParam7.DbType = DbType.String;
            upsertParam7.ParameterName = "@lastError";
            upsertCommand.Parameters.Add(upsertParam7);
            upsertParam1.Value = domain.DomainName;
            upsertParam5.Value = lastAttempt.milliseconds;
            upsertParam6.Value = correlationId;
            upsertParam7.Value = lastError ?? (object)DBNull.Value;

            await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (await rdr.ReadAsync())
            {
                return 1;
            }
            return 0;
        }
    }
}
