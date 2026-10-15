using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Odin.Services.Registry.Registration;

#nullable enable

/// <summary>
/// An invitation code as shown to people: the code in its display form (K7QX-M2PD-9FHW).
/// </summary>
public record InvitationCodeInfo(string Code, string Owner, int MaxUses, int UsesLeft);

/// <summary>
/// The invitation codes of this host (#1908), in a SQLite database of their own, apart from the system
/// database. A use records only when it happened: who signed up with a code is kept on their own
/// registration, not here.
///
/// Every method takes a code as typed (any case, with or without dashes); a code that cannot be one is
/// simply not found.
///
/// Prototype: the codes are this host's alone, so a code created on one cluster does not work on another.
/// </summary>
public class InvitationCodeStore
{
    public const int MaxUsesLimit = 10_000;

    private const int MaxCreateAttempts = 5;
    private const int SqliteConstraintError = 19;

    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    /// <param name="databasePath">The database file; empty turns invitation codes off</param>
    public InvitationCodeStore(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
        }.ToString();
    }

    public bool IsEnabled => _databasePath != "";

    //

    public async Task<InvitationCodeInfo> CreateAsync(string owner, int maxUses)
    {
        if (maxUses is < 1 or > MaxUsesLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUses), $"MaxUses must be between 1 and {MaxUsesLimit}");
        }

        owner = NormalizeOwner(owner);
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
                return new InvitationCodeInfo(InvitationCodeFormat.Display(code), owner, maxUses, maxUses);
            }
            catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraintError && attempt < MaxCreateAttempts)
            {
                // The random code is already taken; draw another
            }
        }
    }

    //

    public async Task<InvitationCodeInfo?> GetAsync(string? input)
    {
        var code = InvitationCodeFormat.Normalize(input);
        if (code == null)
        {
            return null;
        }

        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectWithUsesLeft + " WHERE c.code = @code GROUP BY c.code;";
        command.Parameters.AddWithValue("@code", code);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    //

    /// <summary>
    /// Whether the code exists and has a use left.
    /// </summary>
    public async Task<bool> IsValidAsync(string? input)
    {
        return await GetAsync(input) is { UsesLeft: > 0 };
    }

    //

    public async Task<List<InvitationCodeInfo>> ListByOwnerAsync(string owner)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectWithUsesLeft + " WHERE c.owner = @owner GROUP BY c.code ORDER BY c.created_at;";
        command.Parameters.AddWithValue("@owner", NormalizeOwner(owner));
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<InvitationCodeInfo>();
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
    public async Task<bool> TryUseAsync(string? input)
    {
        var code = InvitationCodeFormat.Normalize(input);
        if (code == null)
        {
            return false;
        }

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
    public async Task UndoUseAsync(string? input)
    {
        var code = InvitationCodeFormat.Normalize(input);
        if (code == null)
        {
            return;
        }

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

    private static InvitationCodeInfo Read(SqliteDataReader reader)
    {
        return new InvitationCodeInfo(
            InvitationCodeFormat.Display(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt32(2),
            Math.Max(0, reader.GetInt32(3)));
    }

    private static string NormalizeOwner(string owner) => owner.Trim().ToLowerInvariant();

    private async Task<SqliteConnection> OpenAsync()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Invitation codes are not enabled on this host");
        }

        await InitializeAsync();
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    // Creates the database on first use
    private async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
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
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }
}
