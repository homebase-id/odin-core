#nullable enable
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Factory;

/// <summary>
/// Keeps disposed-by-caller commands alive per physical connection, keyed by SQL text, so a later
/// command with the same text reuses the already-prepared statement instead of re-parsing it.
/// Only meaningful for providers whose prepared statements live on the command (SQLite).
/// </summary>
internal static class PreparedCommandCache
{
    private const int MaxCommandsPerConnection = 128;
    private static readonly ConditionalWeakTable<DbConnection, Dictionary<string, DbCommand>> Table = new();

    public static DbCommand? TryTake(DbConnection connection, string commandText)
    {
        if (!Table.TryGetValue(connection, out var commands))
        {
            return null;
        }

        lock (commands)
        {
            return commands.Remove(commandText, out var command) ? command : null;
        }
    }

    public static bool TryReturn(DbConnection connection, DbCommand command)
    {
        var text = command.CommandText;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var commands = Table.GetValue(connection, _ => new Dictionary<string, DbCommand>());
        lock (commands)
        {
            if (commands.Count >= MaxCommandsPerConnection || commands.ContainsKey(text))
            {
                return false;
            }

            command.Parameters.Clear();
            command.Transaction = null;
            commands[text] = command;
            return true;
        }
    }

    public static async Task DisposeAllAsync(DbConnection connection)
    {
        if (!Table.TryGetValue(connection, out var commands))
        {
            return;
        }

        Table.Remove(connection);
        List<DbCommand> toDispose;
        lock (commands)
        {
            toDispose = [..commands.Values];
            commands.Clear();
        }

        foreach (var command in toDispose)
        {
            await command.DisposeAsync();
        }
    }
}
