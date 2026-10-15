using Microsoft.Data.Sqlite;

namespace Odin.SetupHelper.InvitationCodes;

public record InvitationCode(string Code, string Owner, int MaxUses, int UsesLeft);

/// <summary>
/// The official record of invitation codes, shared by every cluster's signup (#1908).
/// A use records only when it happened: who signed up with a code is kept on their own
/// registration, not here.
/// </summary>
public class InvitationCodeStore
{
    private const int MaxCreateAttempts = 5;
    private const int SqliteConstraintError = 19;

    private readonly string _connectionString;

    public InvitationCodeStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS invitation_codes (
                code TEXT NOT NULL PRIMARY KEY,
                owner TEXT NOT NULL,
                max_uses INTEGER NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_invitation_codes_owner ON invitation_codes(owner);
            CREATE TABLE IF NOT EXISTS invitation_code_uses (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL,
                used_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_invitation_code_uses_code ON invitation_code_uses(code);
            """;
        await command.ExecuteNonQueryAsync();
    }

    //

    public async Task<InvitationCode> CreateAsync(string owner, int maxUses)
    {
        await using var connection = await OpenAsync();
        for (var attempt = 1; ; attempt++)
        {
            var code = InvitationCodeFormat.Generate();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO invitation_codes (code, owner, max_uses, created_at) VALUES (@code, @owner, @maxUses, @now);";
            command.Parameters.AddWithValue("@code", code);
            command.Parameters.AddWithValue("@owner", owner);
            command.Parameters.AddWithValue("@maxUses", maxUses);
            command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            try
            {
                await command.ExecuteNonQueryAsync();
                return new InvitationCode(code, owner, maxUses, maxUses);
            }
            catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraintError && attempt < MaxCreateAttempts)
            {
                // The random code is already taken; draw another
            }
        }
    }

    //

    public async Task<InvitationCode?> GetAsync(string code)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectWithUsesLeft + " WHERE c.code = @code GROUP BY c.code;";
        command.Parameters.AddWithValue("@code", code);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    //

    public async Task<List<InvitationCode>> ListByOwnerAsync(string owner)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectWithUsesLeft + " WHERE c.owner = @owner GROUP BY c.code ORDER BY c.created_at;";
        command.Parameters.AddWithValue("@owner", owner);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<InvitationCode>();
        while (await reader.ReadAsync())
        {
            result.Add(Read(reader));
        }
        return result;
    }

    //

    /// <summary>
    /// Records a use if the code exists and has one left. One statement, so two signups racing
    /// for the last use cannot both get it.
    /// </summary>
    public async Task<bool> TryUseAsync(string code)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO invitation_code_uses (code, used_at)
            SELECT @code, @now
            WHERE EXISTS (
                SELECT 1 FROM invitation_codes c
                WHERE c.code = @code
                  AND c.max_uses > (SELECT COUNT(*) FROM invitation_code_uses u WHERE u.code = @code)
            );
            """;
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync() == 1;
    }

    //

    /// <summary>
    /// Gives back the latest use, for a signup that failed after taking it.
    /// </summary>
    public async Task UndoUseAsync(string code)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM invitation_code_uses WHERE id = (SELECT MAX(id) FROM invitation_code_uses WHERE code = @code);";
        command.Parameters.AddWithValue("@code", code);
        await command.ExecuteNonQueryAsync();
    }

    //

    private const string SelectWithUsesLeft =
        """
        SELECT c.code, c.owner, c.max_uses, c.max_uses - COUNT(u.id)
        FROM invitation_codes c LEFT JOIN invitation_code_uses u ON u.code = c.code
        """;

    private static InvitationCode Read(SqliteDataReader reader)
    {
        return new InvitationCode(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            Math.Max(0, reader.GetInt32(3)));
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
