using System.Collections.Generic;

namespace Odin.Core.Storage.Database;

public abstract class AbstractMigrator
{
    protected abstract List<MigrationBase> SortedMigrations { get; }
}
