using System;
using System.IO;
using System.Runtime.InteropServices;
using SQLitePCL;

namespace Odin.Core.Storage.Factory.Sqlite;

internal class SqliteNativeLibraryAdapter(string name) : IGetFunctionPointer
{
    private readonly IntPtr _library = NativeLibrary.Load(name);

    public IntPtr GetFunctionPointer(string name) => NativeLibrary.TryGetExport(_library, name, out var address)
        ? address
        : IntPtr.Zero;
}

internal static class LibSqlProvider
{
    private static bool _initialized = false;
    private static object _mutex = new();

    public static void Initialize()
    {
        // https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions?tabs=net-cli#use-the-dynamic-provider

        // ReSharper disable once InconsistentlySynchronizedField
        if (_initialized)
        {
            return;
        }

        lock (_mutex)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            var libPath = "";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var home = Environment.GetEnvironmentVariable("HOME") ?? throw new Exception("oh no");
                libPath = Path.Combine(home, "code/sandbox/libsql/libsql-sqlite3/.libs/liblibsql.so");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var home = Environment.GetEnvironmentVariable("HOME") ?? throw new Exception("oh no");
                libPath = Path.Combine(home, "code/sandbox/libsql/libsql-sqlite3/.libs/liblibsql.dylib");
            }
            else
            {
                throw new Exception("Unsupported OS");
            }

            SQLite3Provider_dynamic_cdecl.Setup("sqlite3", new SqliteNativeLibraryAdapter(libPath));
            raw.SetProvider(new SQLite3Provider_dynamic_cdecl());
            Batteries_V2.Init();
        }
    }
}
