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
    public class TableCircleMigrationV202610140701 : MigrationBase
    {
        public override Int64 MigrationVersion => 202610140701;
        public TableCircleMigrationV202610140701(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE CircleMigrationsV202610140701 IS '{ \"Version\": 202610140701 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS CircleMigrationsV202610140701( -- { \"Version\": 202610140701 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"circleName TEXT NOT NULL, "
                   +"data BYTEA , "
                   +"AppId BYTEA , "
                   +"CircleSlug TEXT NOT NULL, "
                   +"GrantOn BIGINT NOT NULL DEFAULT 0, "
                   +"Designation BIGINT NOT NULL DEFAULT 1, "
                   +"Emoji TEXT  "
                   +", UNIQUE(identityId,circleId)"
                   +", UNIQUE(identityId,AppId,CircleSlug)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0CircleMigrationsV202610140701 ON CircleMigrationsV202610140701(identityId,AppId);"
                   +"CREATE INDEX IF NOT EXISTS Idx1CircleMigrationsV202610140701 ON CircleMigrationsV202610140701(identityId,GrantOn);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "CircleMigrationsV202610140701", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("circleId");
            sl.Add("circleName");
            sl.Add("data");
            sl.Add("AppId");
            sl.Add("CircleSlug");
            sl.Add("GrantOn");
            sl.Add("Designation");
            sl.Add("Emoji");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "CircleMigrationsV202610140701", MigrationVersion);
            await CheckSqlTableVersion(cn, "Circle", PreviousVersion);

            // The old table has no CircleSlug, so the generated INSERT...SELECT cannot fill it.  Plan every
            // slug first, then copy each row across with its slug.  The data blob stays in the database.
            var plan = PlanSlugs(await ReadCirclesAsync(cn));
            AssertPlanIsValid(plan);

            var copied = 0;
            foreach (var (row, slug) in plan)
            {
                await using var copyCommand = cn.CreateCommand();
                copyCommand.CommandText =
                    "INSERT INTO CircleMigrationsV202610140701 (rowId,identityId,circleId,circleName,data,AppId,CircleSlug,GrantOn,Designation,Emoji) " +
                    "SELECT rowId,identityId,circleId,circleName,data,AppId,@circleSlug,GrantOn,Designation,Emoji " +
                    "FROM Circle WHERE rowId=@rowId;";
                copyCommand.AddParameter("@circleSlug", DbType.String, slug);
                copyCommand.AddParameter("@rowId", DbType.Int64, row.RowId);
                if (await copyCommand.ExecuteNonQueryAsync() != 1)
                    throw new MigrationException(
                        $"Circle v202610140701: copying circle {row.CircleId} of identity {row.IdentityId} did not insert one row");
                copied++;
            }

            return copied;
        }

        // ---------------------------------------------------------------------------------------------
        // Giving every existing circle a slug (#1939)
        //
        // Everything below is a FROZEN copy of the slug rules as they stood when this migration was
        // written: the OdinSlug pattern and length, and the built-in circles' slugs.  Those live in
        // Odin.Services, which this project cannot reference, and a migration must not change behaviour
        // when they do.  Do not "fix" it to match them later.
        //
        // A built-in circle gets its fixed slug.  Every other circle gets one made from its name,
        // deduplicated within its owning app; the oldest circle keeps the plain slug and later ones get
        // -2, -3...  A derived slug never takes a built-in one, so a circle named "Chat" cannot block the
        // chat circle from being moved into its app later.  Rows with a NULL AppId are deduplicated with
        // the owner console's: the v18 -> v19 upgrade stamps them to it, which must not then collide.  The
        // table is read without an identity filter: on Postgres every identity shares it and it is
        // migrated once.
        // ---------------------------------------------------------------------------------------------

        private const int SlugMaxLength = 14;
        private const string FallbackSlug = "circle";
        private static readonly Guid OwnerConsoleAppId = Guid.Parse("ac126e09-54cb-4878-a690-856be692da16");

        private static readonly Regex ValidSlug = new("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);
        private static readonly Regex NonSlugRun = new("[^a-z0-9]+", RegexOptions.Compiled);

        // Keyed by circle id.  Internal so a test can hold it to the live declarations.
        internal static readonly IReadOnlyDictionary<Guid, string> KnownCircleSlugs = new Dictionary<Guid, string>
        {
            [Guid.Parse("55900e0ab05347dca85c5ac2514e7fd3")] = "chat",
            [Guid.Parse("3d594614f445f6b00014e9b77730b833")] = "friends",
            [Guid.Parse("e210f78d11b7473f9ca8a837f7da369d")] = "email",
            [Guid.Parse("cb29850cd326480b9c91deb0fbf061d7")] = "feed",
            [Guid.Parse("9a4b1ce10cf64063900442b4bdf9a78b")] = "homepage",
            [Guid.Parse("8b5383a5927246f8a666f4f3fcb7392b")] = "emergency",
            [Guid.Parse("34cd48d94fe446c4a92e1b525b942071")] = "mail",
            [Guid.Parse("c570d12b688f40c685c6da056979cef3")] = "recovery",
            [Guid.Parse("678306e834d0484ab7aabbb98e5c65b2")] = "community",
            [Guid.Parse("ff158d5eef354b4d9a7f08bd9c224b09")] = "lists",
            [Guid.Parse("2942b45424164f85b37842d5e25388ed")] = "moments",
            [Guid.Parse("d9471317728a431c94d9842118acb676")] = "photos",
            [Guid.Parse("94978395c00247cd8c5f32eb4ca61a06")] = "socialsync",
            [Guid.Parse("d6529de8b0e94da5a1f676f272da8346")] = "vault",
            [Guid.Parse("a510f814c6054a5c8bddcc0fbd2f627a")] = "webdrop",

            // The retired system circles, should an identity still have them.
            [Guid.Parse("bb2683fa402aff866e771a6495765a15")] = "confirmed",
            [Guid.Parse("9e22b42952f74d2580e11250b651d343")] = "auto-connected"
        };

        private sealed record CircleRow(long RowId, Guid IdentityId, Guid CircleId, string Name, Guid? AppId);

        private static async Task<List<CircleRow>> ReadCirclesAsync(IConnectionWrapper cn)
        {
            var rows = new List<CircleRow>();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT rowId,identityId,circleId,circleName,AppId FROM Circle ORDER BY rowId;";
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                rows.Add(new CircleRow(
                    Convert.ToInt64(rdr[0]),
                    new Guid((byte[])rdr[1]),
                    new Guid((byte[])rdr[2]),
                    (string)rdr[3],
                    rdr[4] == DBNull.Value ? null : new Guid((byte[])rdr[4])));
            }

            return rows;
        }

        // Rows arrive oldest first.  Built-in circles are planned first so their fixed slugs are in each
        // app's taken set before any name is slugified.
        private static List<(CircleRow Row, string Slug)> PlanSlugs(List<CircleRow> rows)
        {
            var taken = new Dictionary<(Guid IdentityId, Guid AppId), HashSet<string>>();
            HashSet<string> TakenFor(CircleRow row)
            {
                var key = (row.IdentityId, row.AppId ?? OwnerConsoleAppId);
                if (!taken.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    taken[key] = set;
                }

                return set;
            }

            var slugs = new Dictionary<long, string>();
            foreach (var row in rows.Where(r => KnownCircleSlugs.ContainsKey(r.CircleId)))
            {
                var slug = KnownCircleSlugs[row.CircleId];
                if (!TakenFor(row).Add(slug))
                    throw new MigrationException(
                        $"Circle v202610140701: built-in slug '{slug}' is taken twice in identity {row.IdentityId}");
                slugs[row.RowId] = slug;
            }

            foreach (var row in rows.Where(r => !KnownCircleSlugs.ContainsKey(r.CircleId)))
            {
                var set = TakenFor(row);
                var slug = GenerateSlug(row, set);
                set.Add(slug);
                slugs[row.RowId] = slug;
            }

            return rows.Select(r => (r, slugs[r.RowId])).ToList();
        }

        private static string GenerateSlug(CircleRow row, HashSet<string> taken)
        {
            bool IsFree(string candidate) =>
                ValidSlug.IsMatch(candidate) && !taken.Contains(candidate) &&
                !KnownCircleSlugs.Values.Contains(candidate);

            var baseSlug = Slugify(row.Name) ?? FallbackSlug;
            if (IsFree(baseSlug))
                return baseSlug;

            for (var suffix = 2; suffix <= 9999; suffix++)
            {
                var tail = "-" + suffix;
                var trimmed = baseSlug[..Math.Min(baseSlug.Length, SlugMaxLength - tail.Length)].TrimEnd('-');
                var candidate = (trimmed.Length > 0 ? trimmed : FallbackSlug) + tail;
                if (IsFree(candidate))
                    return candidate;
            }

            throw new MigrationException(
                $"Circle v202610140701: could not assign a unique slug to circle {row.CircleId} (name '{row.Name}') " +
                $"in identity {row.IdentityId}");
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

        // A safety net before the copy: names the circles rather than letting it die on a bare constraint
        // violation.
        private static void AssertPlanIsValid(List<(CircleRow Row, string Slug)> plan)
        {
            var offenders = plan
                .Where(p => p.Slug == null || p.Slug.Length > SlugMaxLength || !ValidSlug.IsMatch(p.Slug))
                .Select(p => $"identityId={p.Row.IdentityId} circleId={p.Row.CircleId} name='{p.Row.Name}' slug='{p.Slug}'")
                .ToList();

            if (offenders.Count > 0)
            {
                throw new MigrationException(
                    $"Circle v202610140701 makes CircleSlug NOT NULL, but {offenders.Count} circle(s) were planned " +
                    "an invalid slug: " + string.Join("; ", offenders));
            }
        }

        // Will upgrade from the previous version to version 202610140701
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "CircleMigrationsV202610140701", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Circle", "CircleMigrationsV202610140701") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Circle", $"CircleMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "CircleMigrationsV202610140701", "Circle");
                    await CheckSqlTableVersion(cn, "Circle", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "Circle", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"CircleMigrationsV{PreviousVersion}", "Circle") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "Circle", "CircleMigrationsV202610140701");
                    await SqlHelper.RenameAsync(cn, $"CircleMigrationsV{PreviousVersion}", "Circle");
                    await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
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
