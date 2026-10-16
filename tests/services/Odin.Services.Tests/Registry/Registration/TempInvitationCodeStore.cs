using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Registry.Registration;

/// <summary>
/// An invitation-code store in a temp file, deleted on dispose
/// </summary>
public sealed class TempInvitationCodeStore : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"invitation-codes-{Guid.NewGuid()}.db");

    public TempInvitationCodeStore()
    {
        Store = new InvitationCodeStore(_path);
    }

    public InvitationCodeStore Store { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
