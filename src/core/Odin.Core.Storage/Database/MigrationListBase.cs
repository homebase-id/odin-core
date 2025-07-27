// Base for all container classes (shares prev/next logic).
using Odin.Core.Storage.Database;
using System;
using System.Collections.Generic;

public abstract class MigrationListBase
{
    public List<MigrationBase> Migrations;

    public MigrationListBase()
    {
    }

    public void ValidateMigrationList()
    {
        long prev = -1;
        foreach (var migration in Migrations)
        {
            if (migration.MigrationVersion <= prev)
            {
                Migrations = null;
                throw new Exception("Version numbers not increasing");
            }

            if (migration.PreviousVersion != prev)
            {
                Migrations = null;
                throw new Exception("Previous version not matching list order");
            }

            prev = migration.MigrationVersion;
        }
    }

    public MigrationBase GetByVersion(Int64 version)
    {
        foreach (var o in Migrations)
        {
            if (o.MigrationVersion == version)
            {
                return o;
            }
        }

        return null;
    }

    public MigrationBase GetFirstVersion()
    {
        return Migrations[0];
    }

    public MigrationBase GetLatestVersion()
    {
        return Migrations[Migrations.Count - 1];
    }
}