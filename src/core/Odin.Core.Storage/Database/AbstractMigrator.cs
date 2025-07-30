using System.Collections.Generic;

namespace Odin.Core.Storage.Database;

public abstract class AbstractMigrator
{
    public abstract List<MigrationBase> SortedMigrations { get; }
}
