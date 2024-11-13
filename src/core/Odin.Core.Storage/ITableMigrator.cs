using System.Threading.Tasks;

namespace Odin.Core.Storage;

public interface ITableMigrator
{
    // SEB:NOTE this is temporary until we have a proper migration system
    public Task EnsureTableExistsAsync(bool dropExisting);
}