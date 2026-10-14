using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.Identity.Connection;

#nullable disable

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableDrivesMigrationV202610130807 : MigrationBase
    {
        public override Int64 MigrationVersion => 202610130807;
        public TableDrivesMigrationV202610130807(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DrivesMigrationsV202610130807 IS '{ \"Version\": 202610130807 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DrivesMigrationsV202610130807( -- { \"Version\": 202610130807 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"DriveId BYTEA NOT NULL, "
                   +"StorageKeyCheckValue BYTEA NOT NULL, "
                   +"DriveType BYTEA NOT NULL, "
                   +"DriveName TEXT NOT NULL, "
                   +"MasterKeyEncryptedStorageKeyJson TEXT NOT NULL, "
                   +"EncryptedIdIv64 TEXT NOT NULL, "
                   +"EncryptedIdValue64 TEXT NOT NULL, "
                   +"detailsJson TEXT NOT NULL, "
                   +"AppId BYTEA NOT NULL, "
                   +"DriveSlug TEXT NOT NULL, "
                   +"DriveTypeSlug TEXT NOT NULL, "
                   +"WriteOnlyKeyPair BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,DriveId)"
                   +", UNIQUE(identityId,AppId,DriveSlug)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0DrivesMigrationsV202610130807 ON DrivesMigrationsV202610130807(identityId,DriveType);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DrivesMigrationsV202610130807", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("DriveId");
            sl.Add("StorageKeyCheckValue");
            sl.Add("DriveType");
            sl.Add("DriveName");
            sl.Add("MasterKeyEncryptedStorageKeyJson");
            sl.Add("EncryptedIdIv64");
            sl.Add("EncryptedIdValue64");
            sl.Add("detailsJson");
            sl.Add("AppId");
            sl.Add("DriveSlug");
            sl.Add("DriveTypeSlug");
            sl.Add("WriteOnlyKeyPair");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DrivesMigrationsV202610130807", MigrationVersion);
            await CheckSqlTableVersion(cn, "Drives", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO DrivesMigrationsV202610130807 (rowId,identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,AppId,DriveSlug,DriveTypeSlug,WriteOnlyKeyPair,created,modified) " +
               $"SELECT rowId,identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,AppId,DriveSlug,DriveTypeSlug,WriteOnlyKeyPair,created,modified "+
               $"FROM Drives;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Filling the NULL addresses (#1901)
        //
        // Everything below is a FROZEN copy of the drive passes the v13 -> v14 and v18 -> v19 upgrades
        // used to run before this migration took the job over: user channel drives go to the feed app,
        // every other drive with no owner to the owner console, and a missing slug or type slug is
        // derived.  The rules, ids and built-in slugs live in Odin.Services, which this project cannot
        // reference, and a migration must not change behaviour when they do.  Do not "fix" it to match
        // them later.
        //
        // It runs here, not at owner sign-in, so an identity whose owner has not signed in is not left
        // with NULLs this migration cannot carry.  The table is read without an identity filter: on
        // Postgres every identity shares it and it is migrated once.
        //
        // One difference from those passes: a derived slug never takes a built-in drive's slug.  v13 ->
        // v14 still moves built-in drives into their apps afterwards (it stamps from the app tree, which
        // this cannot see), and it used to run first; reserving the built-in slugs keeps that move from
        // colliding with a channel drive named, say, "Feed".
        // ---------------------------------------------------------------------------------------------

        private static readonly Guid OwnerConsoleAppId = Guid.Parse("ac126e09-54cb-4878-a690-856be692da16");
        private static readonly Guid FeedAppId = Guid.Parse("5f887d80-0132-4294-ba40-bda79155551d");
        private static readonly Guid ChannelDriveType = Guid.Parse("8f448716-e34c-edf9-0141-45e043ca6612");
        private const string DefaultTypeSlug = "drive";
        private const int SlugMaxLength = 14;

        private static readonly Regex ValidSlug = new("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);
        private static readonly Regex NonSlugRun = new("[^a-z0-9]+", RegexOptions.Compiled);

        // Keyed by drive id, which is the drive's alias.
        private static readonly Dictionary<Guid, string> KnownDriveSlugs = new()
        {
            [Guid.Parse("9ff813aff2d61e2f9b9db189e72d1a11")] = "chat",
            [Guid.Parse("3b9c5f2e-7a41-4d6b-9e0c-8f1a2b3c4d5e")] = "stickers",
            [Guid.Parse("3e5de26f8fa343c1975ad0dd2aa8564c")] = "community",
            [Guid.Parse("2612429d1c3f037282b8d42fb2cc0499")] = "contacts",
            [Guid.Parse("8f12d8c4933813d378488d91ed23b64c")] = "profile",
            [Guid.Parse("92bbcad8-3558-417b-9376-9976c086a674")] = "email",
            [Guid.Parse("4db49422ebad02e99ab96e9c477d1e08")] = "feed",
            [Guid.Parse("e8475dc46cb4b6651c2d0dbd0f3aad5f")] = "posts",
            [Guid.Parse("ec83345af6a747d4404ef8b0f8844caa")] = "home",
            [Guid.Parse("a44e7a2651f44a26ad125d7627b35d0e")] = "lists",
            [Guid.Parse("2e191a14-8640-4ebc-b0c8-aaac913f6fa8")] = "location",
            [Guid.Parse("e69b5a48a663482fbfd846f3b0b143b0")] = "mail",
            [Guid.Parse("a85f8562-6c74-4947-896b-619812cafccc")] = "moments",
            [Guid.Parse("46242d0d67604b2aa683f05cd48d4aef")] = "shard-recovery",
            [Guid.Parse("a6f991e214b11c8c9796f664e1ec0cac")] = "wallet",
            [Guid.Parse("6483b7b1f71bd43eb6896c86148668cc")] = "photos",
            [Guid.Parse("f47ac10b58cc4372a5670e02b2c3d479")] = "vault",
            [Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c")] = "transient"
        };

        private static readonly Dictionary<Guid, string> KnownDriveTypeSlugs = new()
        {
            [Guid.Parse("9ff813aff2d61e2f9b9db189e72d1a11")] = "chat",
            [Guid.Parse("3b9c5f2e-7a41-4d6b-9e0c-8f1a2b3c4d5e")] = "sticker",
            [Guid.Parse("3e5de26f8fa343c1975ad0dd2aa8564c")] = "community",
            [Guid.Parse("2612429d1c3f037282b8d42fb2cc0499")] = "contact",
            [Guid.Parse("8f12d8c4933813d378488d91ed23b64c")] = "profile",
            [Guid.Parse("92bbcad8-3558-417b-9376-9976c086a674")] = "email",
            [Guid.Parse("4db49422ebad02e99ab96e9c477d1e08")] = "feed",
            [Guid.Parse("e8475dc46cb4b6651c2d0dbd0f3aad5f")] = "channel",
            [Guid.Parse("ec83345af6a747d4404ef8b0f8844caa")] = "profile",
            [Guid.Parse("a44e7a2651f44a26ad125d7627b35d0e")] = "list",
            [Guid.Parse("2e191a14-8640-4ebc-b0c8-aaac913f6fa8")] = "location",
            [Guid.Parse("e69b5a48a663482fbfd846f3b0b143b0")] = "mail",
            [Guid.Parse("a85f8562-6c74-4947-896b-619812cafccc")] = "list",
            [Guid.Parse("46242d0d67604b2aa683f05cd48d4aef")] = "shard-recovery",
            [Guid.Parse("a6f991e214b11c8c9796f664e1ec0cac")] = "profile",
            [Guid.Parse("6483b7b1f71bd43eb6896c86148668cc")] = "photos",
            [Guid.Parse("f47ac10b58cc4372a5670e02b2c3d479")] = "vault",
            [Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c")] = "transient"
        };

        private sealed record DriveRow(Guid DriveId, string Name, Guid DriveType, Guid? AppId, string DriveSlug,
            string DriveTypeSlug);

        private static async Task<int> FillNullAddressingAsync(IConnectionWrapper cn)
        {
            // Same order the v18 -> v19 upgrade reads them in, so a slug collision resolves the same way.
            var byIdentity = new Dictionary<Guid, List<DriveRow>>();
            await using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT identityId,DriveId,DriveName,DriveType,AppId,DriveSlug,DriveTypeSlug FROM Drives " +
                    "ORDER BY identityId, created DESC, rowId DESC;";
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    var identityId = new Guid((byte[])rdr[0]);
                    if (!byIdentity.TryGetValue(identityId, out var rows))
                    {
                        rows = new List<DriveRow>();
                        byIdentity[identityId] = rows;
                    }

                    rows.Add(new DriveRow(
                        new Guid((byte[])rdr[1]),
                        (string)rdr[2],
                        new Guid((byte[])rdr[3]),
                        rdr[4] == DBNull.Value ? null : new Guid((byte[])rdr[4]),
                        rdr[5] == DBNull.Value ? null : (string)rdr[5],
                        rdr[6] == DBNull.Value ? null : (string)rdr[6]));
                }
            }

            var filled = 0;
            foreach (var (identityId, rows) in byIdentity)
            {
                foreach (var (driveId, appId, slug, typeSlug) in PlanAddresses(rows))
                {
                    await using var update = cn.CreateCommand();
                    update.CommandText =
                        "UPDATE Drives SET AppId=@appId, DriveSlug=@driveSlug, DriveTypeSlug=@driveTypeSlug " +
                        "WHERE identityId=@identityId AND DriveId=@driveId;";
                    update.AddParameter("@appId", DbType.Binary, appId);
                    update.AddParameter("@driveSlug", DbType.String, slug);
                    update.AddParameter("@driveTypeSlug", DbType.String, typeSlug);
                    update.AddParameter("@identityId", DbType.Binary, identityId);
                    update.AddParameter("@driveId", DbType.Binary, driveId);
                    if (await update.ExecuteNonQueryAsync() != 1)
                        throw new MigrationException(
                            $"Drives v202610130807: filling drive {driveId} of identity {identityId} did not update one row");
                    filled++;
                }
            }

            return filled;
        }

        // One identity's drives.  Uniqueness is per owning app, so each app's taken set starts with the
        // slugs it already holds and grows as drives in this run are assigned.
        private static List<(Guid DriveId, Guid AppId, string DriveSlug, string DriveTypeSlug)> PlanAddresses(
            List<DriveRow> drives)
        {
            var takenByApp = drives
                .Where(d => d.AppId != null && !string.IsNullOrWhiteSpace(d.DriveSlug))
                .GroupBy(d => d.AppId.Value)
                .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(d => d.DriveSlug), StringComparer.Ordinal));

            var result = new List<(Guid, Guid, string, string)>();
            foreach (var drive in drives)
            {
                var carried = string.IsNullOrWhiteSpace(drive.DriveSlug) ? null : drive.DriveSlug;
                var carriedTypeSlug = string.IsNullOrWhiteSpace(drive.DriveTypeSlug) ? null : drive.DriveTypeSlug;

                if (drive.AppId != null && carried != null && carriedTypeSlug != null)
                    continue;

                var appId = drive.AppId ?? (drive.DriveType == ChannelDriveType ? FeedAppId : OwnerConsoleAppId);
                if (!takenByApp.TryGetValue(appId, out var taken))
                {
                    taken = new HashSet<string>(StringComparer.Ordinal);
                    takenByApp[appId] = taken;
                }

                // A slug from the ownerless era was outside the unique constraint, so it may collide with
                // one the app it now goes to holds; it is re-derived then.
                if (drive.AppId == null && carried != null && taken.Contains(carried))
                    carried = null;

                var slug = carried ?? GenerateSlug(drive.DriveId, drive.Name, taken);
                taken.Add(slug);

                var typeSlug = carriedTypeSlug ?? TypeSlugOrDefault(drive.DriveId, drive.DriveType);
                result.Add((drive.DriveId, appId, slug, typeSlug));
            }

            return result;
        }

        private static string TypeSlugOrDefault(Guid driveId, Guid driveType)
        {
            if (KnownDriveTypeSlugs.TryGetValue(driveId, out var slug))
                return slug;
            return driveType == ChannelDriveType ? "channel" : DefaultTypeSlug;
        }

        private static string GenerateSlug(Guid driveId, string name, HashSet<string> taken)
        {
            if (KnownDriveSlugs.TryGetValue(driveId, out var known))
            {
                if (taken.Contains(known))
                    throw new MigrationException($"System drive slug '{known}' is already taken; cannot assign it to {driveId}");
                return known;
            }

            // The built-in slugs are reserved: see the note at the top of this section.
            taken = new HashSet<string>(taken.Concat(KnownDriveSlugs.Values), StringComparer.Ordinal);

            var baseSlug = Slugify(name);
            if (baseSlug != null && !taken.Contains(baseSlug))
                return baseSlug;

            if (baseSlug != null)
            {
                for (var suffix = 2; suffix <= 99; suffix++)
                {
                    var tail = "-" + suffix;
                    var trimmed = baseSlug[..Math.Min(baseSlug.Length, SlugMaxLength - tail.Length)].TrimEnd('-');
                    if (trimmed.Length == 0)
                        break;

                    var candidate = trimmed + tail;
                    if (ValidSlug.IsMatch(candidate) && !taken.Contains(candidate))
                        return candidate;
                }
            }

            var fromId = driveId.ToString("N")[..SlugMaxLength];
            if (!taken.Contains(fromId) && ValidSlug.IsMatch(fromId))
                return fromId;

            throw new MigrationException($"Could not assign a unique slug to drive {driveId} (name '{name}')");
        }

        private static string Slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var lowered = new StringBuilder(name.Length);
            foreach (var c in name.ToLowerInvariant())
                lowered.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-');

            var collapsed = NonSlugRun.Replace(lowered.ToString(), "-").Trim('-');
            if (collapsed.Length > SlugMaxLength)
                collapsed = collapsed[..SlugMaxLength].TrimEnd('-');

            return collapsed.Length > 0 && ValidSlug.IsMatch(collapsed) ? collapsed : null;
        }

        // A safety net after the fill: names the drives rather than letting the copy die on a bare NOT NULL
        // violation.
        private static async Task AssertNoNullAddressingAsync(IConnectionWrapper cn)
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT identityId,DriveId FROM Drives " +
                              "WHERE AppId IS NULL OR DriveSlug IS NULL OR DriveTypeSlug IS NULL;";
            var offenders = new List<string>();
            await using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    offenders.Add($"identityId={new Guid((byte[])rdr[0])} driveId={new Guid((byte[])rdr[1])}");
                }
            }

            if (offenders.Count > 0)
            {
                throw new MigrationException(
                    $"Drives v202610130807 makes AppId, DriveSlug and DriveTypeSlug NOT NULL, but {offenders.Count} " +
                    "drive(s) still have a NULL in one of them after the fill. Drives: " + string.Join("; ", offenders));
            }
        }

        // Will upgrade from the previous version to version 202610130807
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Drives", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await FillNullAddressingAsync(cn);
                    await AssertNoNullAddressingAsync(cn);
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "DrivesMigrationsV202610130807", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Drives", "DrivesMigrationsV202610130807") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Drives", $"DrivesMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "DrivesMigrationsV202610130807", "Drives");
                    await CheckSqlTableVersion(cn, "Drives", MigrationVersion);
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Drives", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"DrivesMigrationsV{PreviousVersion}", "Drives") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "Drives", "DrivesMigrationsV202610130807");
                    await SqlHelper.RenameAsync(cn, $"DrivesMigrationsV{PreviousVersion}", "Drives");
                    await CheckSqlTableVersion(cn, "Drives", PreviousVersion);
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
