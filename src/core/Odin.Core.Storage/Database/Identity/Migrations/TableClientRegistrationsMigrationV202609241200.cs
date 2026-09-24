using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.Identity.Connection;

#nullable disable

// THIS FILE WAS INITIALLY AUTO GENERATED
// HAND-EDITED: same columns as the previous version; Up also carries the YouAuth domain clients in
// from KeyThreeValue (MoveLegacyClientsAsync), and Down does not count rows.

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    /// <summary>
    /// Moves YouAuth domain clients into ClientRegistrations, the store that enforces expiry.
    /// </summary>
    /// <remarks>
    /// Until now a domain client -- the server half of the token a third-party site holds after
    /// "Sign in with Homebase" -- was a JSON blob in KeyThreeValue, which never reads an expiry back, so
    /// those tokens never expired. Every other client token already lives here. This carries the
    /// existing ones across so nobody is signed out by the switch, and does it at server start rather
    /// than in the owner-login version ladder, because a third party's login has to keep working
    /// before the owner next signs in.
    /// <para>
    /// Each token is sized by its domain's consent, the same rule <c>YouAuthDomainClient.Create</c>
    /// applies to a new one: an Expiring consent puts its date on the token, anything else gets six
    /// months that use restarts. A token whose consent date has already passed is expired, not carried.
    /// </para>
    /// <para>
    /// The KeyThreeValue rows are left in place, as the circle-definition move did: if this goes wrong
    /// the source is still there. Nothing reads them any more. Cleaning them up is a separate job.
    /// </para>
    /// </remarks>
    public class TableClientRegistrationsMigrationV202609241200 : MigrationBase
    {
        public override Int64 MigrationVersion => 202609241200;

        // The two ThreeKeyValueStorage contexts YouAuthDomainRegistrationService used before this
        // version. A key1 is the record's own id followed by its context key.
        private static readonly byte[] LegacyClientContextKey = Guid.Parse("8994c20a-179c-469c-a3b9-c4d6a8d2eb3c").ToByteArray();
        private static readonly byte[] LegacyClientDataType = Guid.Parse("cd16bc37-3e1f-410b-be03-7bec83dd6c33").ToByteArray();
        private static readonly byte[] LegacyDomainContextKey = Guid.Parse("e11ff091-0edf-4532-8b0f-b9d9ebe0880f").ToByteArray();

        // YouAuthDomainClient.CatType and CategoryIdValue, spelled out: a migration must not move
        // when the service does.
        private const int DomainClientCatType = 408;
        private static readonly byte[] DomainClientCategoryId = Guid.Parse("83742ae7-e66d-45e6-82a6-6a003c960b39").ToByteArray();

        private static readonly TimeSpan SlidingLifetime = TimeSpan.FromDays(180);
        public TableClientRegistrationsMigrationV202609241200(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE ClientRegistrationsMigrationsV202609241200 IS '{ \"Version\": 202609241200 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS ClientRegistrationsMigrationsV202609241200( -- { \"Version\": 202609241200 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"catId BYTEA NOT NULL UNIQUE, "
                   +"issuedToId TEXT NOT NULL, "
                   +"ttl BIGINT NOT NULL, "
                   +"expiresAt BIGINT NOT NULL, "
                   +"categoryId BYTEA NOT NULL, "
                   +"catType BIGINT NOT NULL, "
                   +"value TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,catId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "ClientRegistrationsMigrationsV202609241200", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("catId");
            sl.Add("issuedToId");
            sl.Add("ttl");
            sl.Add("expiresAt");
            sl.Add("categoryId");
            sl.Add("catType");
            sl.Add("value");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ClientRegistrationsMigrationsV202609241200", MigrationVersion);
            await CheckSqlTableVersion(cn, "ClientRegistrations", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO ClientRegistrationsMigrationsV202609241200 (rowId,identityId,catId,issuedToId,ttl,expiresAt,categoryId,catType,value,created,modified) " +
               $"SELECT rowId,identityId,catId,issuedToId,ttl,expiresAt,categoryId,catType,value,created,modified "+
               $"FROM ClientRegistrations;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        private sealed record LegacyClient(byte[] IdentityId, byte[] TokenId, string Json);

        private static async Task<List<LegacyClient>> ReadLegacyClientsAsync(IConnectionWrapper cn)
        {
            var clients = new List<LegacyClient>();

            await using var select = cn.CreateCommand();
            select.CommandText = "SELECT identityId,key1,data FROM KeyThreeValue WHERE key3 = @dataType;";
            select.AddParameter("@dataType", DbType.Binary, LegacyClientDataType);

            await using var rdr = await select.ExecuteReaderAsync(CommandBehavior.Default);
            while (await rdr.ReadAsync())
            {
                var identityId = (byte[])rdr[0];
                var key1 = (byte[])rdr[1];
                if (rdr.IsDBNull(2) || key1.Length != 32 || !key1.AsSpan(16).SequenceEqual(LegacyClientContextKey))
                {
                    continue;
                }

                clients.Add(new LegacyClient(identityId, key1.AsSpan(0, 16).ToArray(),
                    global::System.Text.Encoding.UTF8.GetString((byte[])rdr[2])));
            }

            return clients;
        }

        /// <summary>
        /// The domain registration's consent block, or null when the domain has none stored.
        /// </summary>
        private static async Task<JsonObject> ReadConsentAsync(IConnectionWrapper cn, byte[] identityId, string domain)
        {
            // Same key the service uses: GuidId.FromString(domain), the reduced SHA-256 of the lower-cased name.
            var key1 = ByteArrayUtil.Combine(ByteArrayUtil.ReduceSHA256Hash(domain.ToLower()).ToByteArray(), LegacyDomainContextKey);

            await using var select = cn.CreateCommand();
            select.CommandText = "SELECT data FROM KeyThreeValue WHERE identityId = @identityId AND key1 = @key1;";
            select.AddParameter("@identityId", DbType.Binary, identityId);
            select.AddParameter("@key1", DbType.Binary, key1);

            var data = await select.ExecuteScalarAsync();
            if (data is not byte[] bytes)
            {
                return null;
            }

            return JsonNode.Parse(bytes)?.AsObject()?["consentRequirements"]?.AsObject();
        }

        private static async Task<bool> RowExistsAsync(IConnectionWrapper cn, byte[] identityId, byte[] tokenId)
        {
            await using var select = cn.CreateCommand();
            select.CommandText = "SELECT 1 FROM ClientRegistrationsMigrationsV202609241200 WHERE identityId = @identityId AND catId = @catId;";
            select.AddParameter("@identityId", DbType.Binary, identityId);
            select.AddParameter("@catId", DbType.Binary, tokenId);
            return await select.ExecuteScalarAsync() != null;
        }

        /// <summary>
        /// Carries each legacy client into the new table, sized by its domain's consent. Returns how
        /// many rows it wrote. Re-runnable: a token already in the table is skipped.
        /// </summary>
        public async Task<int> MoveLegacyClientsAsync(IConnectionWrapper cn)
        {
            var moved = 0;
            var now = UnixTimeUtc.Now();

            foreach (var legacy in await ReadLegacyClientsAsync(cn))
            {
                if (await RowExistsAsync(cn, legacy.IdentityId, legacy.TokenId))
                {
                    continue;
                }

                var client = JsonNode.Parse(legacy.Json)?.AsObject();
                var domain = client?["domain"]?.GetValue<string>();
                if (client == null || string.IsNullOrEmpty(domain))
                {
                    continue;
                }

                var consent = await ReadConsentAsync(cn, legacy.IdentityId, domain);
                var expiring = string.Equals(consent?["consentRequirementType"]?.GetValue<string>(), "expiring",
                    StringComparison.OrdinalIgnoreCase);

                UnixTimeUtc expiresAt;
                if (expiring)
                {
                    expiresAt = new UnixTimeUtc(consent["expiration"]?.GetValue<long>() ?? 0);
                    if (expiresAt <= now)
                    {
                        // The owner's date has passed. The token is expired, not carried.
                        continue;
                    }
                }
                else
                {
                    expiresAt = now.AddMilliseconds((long)SlidingLifetime.TotalMilliseconds);
                }

                var ttl = (long)(expiresAt - now).TotalSeconds;

                // The fields YouAuthDomainClient reads back that the previous shape did not carry.
                client["timeToLiveSeconds"] = ttl;
                client["slidingExpiration"] = !expiring;

                await using var insert = cn.CreateCommand();
                var sqlNow = insert.SqlNow();
                insert.CommandText =
                    "INSERT INTO ClientRegistrationsMigrationsV202609241200 (identityId,catId,issuedToId,ttl,expiresAt,categoryId,catType,value,created,modified) " +
                    $"VALUES (@identityId,@catId,@issuedToId,@ttl,@expiresAt,@categoryId,@catType,@value,{sqlNow},{sqlNow});";
                insert.AddParameter("@identityId", DbType.Binary, legacy.IdentityId);
                insert.AddParameter("@catId", DbType.Binary, legacy.TokenId);
                insert.AddParameter("@issuedToId", DbType.String, domain);
                insert.AddParameter("@ttl", DbType.Int64, ttl);
                insert.AddParameter("@expiresAt", DbType.Int64, expiresAt.milliseconds);
                insert.AddParameter("@categoryId", DbType.Binary, DomainClientCategoryId);
                insert.AddParameter("@catType", DbType.Int32, DomainClientCatType);
                insert.AddParameter("@value", DbType.String, client.ToJsonString());
                moved += await insert.ExecuteNonQueryAsync();
            }

            return moved;
        }

        // Will upgrade from the previous version to version 202609241200
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ClientRegistrations", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "ClientRegistrationsMigrationsV202609241200", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "ClientRegistrations", "ClientRegistrationsMigrationsV202609241200") == false)
                        throw new MigrationException("Mismatching row counts");

                    // The copy carried explicit rowIds; the rows added next must not collide with them.
                    await SqlHelper.ResyncRowIdSequenceAsync(cn, "ClientRegistrationsMigrationsV202609241200");
                    await MoveLegacyClientsAsync(cn);

                    await SqlHelper.RenameAsync(cn, "ClientRegistrations", $"ClientRegistrationsMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "ClientRegistrationsMigrationsV202609241200", "ClientRegistrations");
                    await CheckSqlTableVersion(cn, "ClientRegistrations", MigrationVersion);
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Restores the previous table. Row counts are not compared: Up added the domain clients, so
        /// they differ by design. Those clients go back to being what they were, rows in KeyThreeValue
        /// that the previous version reads and never expires.
        /// </summary>
        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ClientRegistrations", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await SqlHelper.RenameAsync(cn, "ClientRegistrations", "ClientRegistrationsMigrationsV202609241200");
                    await SqlHelper.RenameAsync(cn, $"ClientRegistrationsMigrationsV{PreviousVersion}", "ClientRegistrations");
                    await CheckSqlTableVersion(cn, "ClientRegistrations", PreviousVersion);
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

    }
}
