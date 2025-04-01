using System;
using System.IO;
using System.Linq;

namespace Odin.Core.Storage.SQLite.Migrations;

// Local test:
//
//   mkdir -p $HOME/tmp/example/data/tenants/{registrations,payloads/shard1,temp}
//   rsync -rvz yagni.dk:/identity-host/data/tenants/temp $HOME/tmp/example/data/tenants
//
//   rsync -rvz yagni.dk:/identity-host/data/tenants/payloads $HOME/tmp/example/data/tenants/payloads
//   yagni: rsync -rvz yagni.dk:/identity-host/data/tenants/payloads/shard1/e689b15f-33b6-4032-b987-4f7018401554 $HOME/tmp/example/data/tenants/payloads/shard1
// run params:
//   --flatten-directories $HOME/tmp/example/data

// PROD:
//
// run params:
//   --flatten-directories /identity-host/data


// Structure PAYLOADS:
/*
  /identity-host/data/tenants/
    ├── payloads
    │   ├── shard1
    │   │   ├── e689b15f-33b6-4032-b987-4f7018401554 <-- tenantId
    │   │   │   ├── drives
    │   │   │   │   ├── 0374b698e6794eadb25502e1b31c6e02 <-- driveId
    │   │   │   │   │    ├── files
    │   │   │   │   │    │   ├── b
    │   │   │   │   │    │   │   ├── c
    │   │   │   │   │    │   │   │   ├── 10000001-1002-1003-1004-123456789abc <-- fileId
    │   │   │   │   │    │   ├── e
    │   │   │   │   │    │   │   ├── f
    │   │   │   │   │    │   │   │   ├── 20000001-2002-2003-2004-123456789def <-- fileId
 */

// Structure TEMP:
/*
  /identity-host/data/tenants/
    ├── temp
    │   ├── upload
    │   │   ├── tenant_id~drive_id~file_id(optional extension etc)
    │   │   ├── tenant_id~drive_id~file_id(optional extension etc)
    │   │   ├── tenant_id~drive_id~file_id(optional extension etc)
    │   ├── inbox
    │   │   ├── tenant_id~drive_id~file_id(optional extension etc)
    │   │   ├── tenant_id~drive_id~file_id(optional extension etc)
    │   │   ├── tenant_id~drive_id~file_id(optional extension etc)
 */

public static class FlattenDirectories
{
    private static bool _dryRun = true;

    public static void Execute(string dataRootPath, bool dryRun)
    {
        _dryRun = dryRun;

        var tenantDirs = Directory.GetDirectories(Path.Combine(dataRootPath, "tenants", "payloads", "shard1"));
        foreach (var tenantDir in tenantDirs)
        {
            WalkTenantPayloadDir(tenantDir);
        }

        tenantDirs = Directory.GetDirectories(Path.Combine(dataRootPath, "tenants", "temp"));
        foreach (var tenantDir in tenantDirs)
        {
            WalkTenantTempDir(tenantDir, Path.Combine(dataRootPath, "tenants", "temp"));
            Console.WriteLine($"  Deleting {tenantDir}");
            if (!_dryRun)
            {
                Directory.Delete(tenantDir, true);
            }

        }

    }

    //

    private static void WalkTenantPayloadDir(string tenantDir)
    {
        Console.WriteLine($"{tenantDir}");

        if (!Directory.Exists(Path.Combine(tenantDir, "drives")))
        {
            return;
        }

        var drives = Directory.GetDirectories(Path.Combine(tenantDir, "drives"));
        foreach (var drive in drives)
        {
            Console.WriteLine($" {drive}");

            var fourCharDirs = Directory.GetDirectories(Path.Combine(drive, "files"))
                .Where(x => Path.GetFileName(x).Length == 4);
            foreach (var fourCharDir in fourCharDirs)
            {
                Console.WriteLine($"  Entering{fourCharDir}");
                FlattenPayloadTree(fourCharDir, Path.Combine(drive, "files"));

                Console.WriteLine($"  Deleting {fourCharDir}");
                if (!_dryRun)
                {
                    Directory.Delete(fourCharDir, true);
                }
            }
        }
    }

    //

    private static void FlattenPayloadTree(string src, string dst)
    {
        var dirs = Directory.GetDirectories(src);
        foreach (var dir in dirs)
        {
            FlattenPayloadTree(dir, dst);
        }

        var files = Directory.GetFiles(src);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);

            // NOTE: we're using string indexing here to be sure we don't mess up casing
            var guidPart = fileName[..32];
            var lastNibble = guidPart[31..32];
            var secondLastNibble = guidPart[30..31];

            var target = Path.Combine(dst, secondLastNibble, lastNibble);
            Console.WriteLine($"  Creating directory {target}");
            if (!_dryRun)
            {
                Directory.CreateDirectory(target);
            }

            target = Path.Combine(target, fileName);
            Console.WriteLine($"  Moving {file} to {target}");
            if (!_dryRun)
            {
                if (!File.Exists(target))
                {
                    File.Move(file, target);
                }
            }
        }
    }

    //

    private static void WalkTenantTempDir(string tenantDir, string dst)
    {
        Console.WriteLine($"{tenantDir}");

        if (!Directory.Exists(Path.Combine(tenantDir, "drives")))
        {
            return;
        }

        var tenant = Path.GetFileName(tenantDir);

        var driveDirs = Directory.GetDirectories(Path.Combine(tenantDir, "drives"));
        foreach (var driveDir in driveDirs)
        {
            Console.WriteLine($" {driveDir}");
            var drive = Path.GetFileName(driveDir);

            var fourCharDirs = Directory.GetDirectories(Path.Combine(driveDir, "files"))
                .Where(x => Path.GetFileName(x).Length == 4);
            foreach (var fourCharDir in fourCharDirs)
            {
                Console.WriteLine($"  Entering{fourCharDir}");
                FlattenTempTree(fourCharDir, dst, tenant, drive);
            }
        }
    }

    private static void FlattenTempTree(string src, string dst, string tenant, string drive)
    {
        var dirs = Directory.GetDirectories(src);
        foreach (var dir in dirs)
        {
            FlattenTempTree(dir, dst, tenant, drive);
        }

        var files = Directory.GetFiles(src);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);

            var target = Path.Combine(dst, tenant + "~" + drive + "~" + fileName);
            Console.WriteLine($"  Moving {file} to {target}");
            if (!_dryRun)
            {
                if (!File.Exists(target))
                {
                    File.Move(file, target);
                }
            }
        }
    }



}