using System.Threading.Tasks;
using Odin.Services.Base;

namespace Odin.Services.ShamiraPasswordRecovery;

/// <summary>
/// Handles scenarios where the Owner has lost their master password and need to get shards from their peer network
/// </summary>
public class ShamirRecoveryService()
{
    /// <summary>
    /// Sets the identity int recovery mode so peer shard holders can give the parts
    /// </summary>
    /// <param name="odinContext"></param>
    public async Task EnterRecoveryMode(IOdinContext odinContext)
    {
        await Task.CompletedTask;
        // TODO
    }
}