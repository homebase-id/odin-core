using System.Threading.Tasks;
using Odin.Services.Base;

namespace Odin.Services.Configuration.VersionUpgrade;

public interface IVersionMigrationService
{
    public Task Upgrade(IOdinContext odinContext);
}