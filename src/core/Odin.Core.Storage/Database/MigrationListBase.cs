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
        int prev = -1;
        foreach (var migration in Migrations)
        {
            if (migration.MigrationVersion <= prev)
            {
                Migrations = null;
                throw new Exception("Version numbers not increasing");
            }
            prev = migration.MigrationVersion;
        }
    }

    public MigrationBase PreviousVersion(MigrationBase current)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));

        int index = -1;
        for (int i = 0; i < Migrations.Count; i++)
        {
            if (Migrations[i] == current)
            {
                index = i;
                break;
            }
        }

        if (index == -1)
        {
            throw new ArgumentException("The provided migration was not found in the list.");
        }

        if (index > 0)
        {
            return Migrations[index - 1];
        }

        return null;
    }

    public int PreviousVersionInt(MigrationBase current)
    {
        MigrationBase o = PreviousVersion(current);

        if (o == null)
            return -1;
        else
            return o.MigrationVersion;
    }

    public MigrationBase NextVersion(MigrationBase current)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));

        int index = -1;
        for (int i = 0; i < Migrations.Count; i++)
        {
            if (Migrations[i] == current)
            {
                index = i;
                break;
            }
        }

        if (index == -1)
        {
            throw new ArgumentException("The provided migration was not found in the list.");
        }

        if (index < Migrations.Count - 1)
        {
            return Migrations[index + 1];
        }

        return null;
    }

    public MigrationBase GetByVersion(int version)
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

    public MigrationBase GetLatestVersion()
    {
        return Migrations[Migrations.Count - 1];
    }
}