using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Query.LiteDb;

namespace Youverse.Core.Services.Drive.Storage
{
    public interface IDriveMetadataIndexer
    {
        Task Rebuild(StorageDriveIndex index);
    }
}