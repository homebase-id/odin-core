using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database;

public interface IGlobalMigrationList
{
    List<MigrationBase> SortedMigrations { get; init; }
}

public class Migrator
{

}