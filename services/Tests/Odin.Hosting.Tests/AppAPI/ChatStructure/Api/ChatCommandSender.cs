using System.Threading.Tasks;
namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatCommandSender
{
    private readonly ChatServerContext _serverContext;

    public ChatCommandSender(ChatServerContext serverContext)
    {
        _serverContext = serverContext;
    }

    // 

    public async Task SendCommand(CommandBase cmd)
    {
        var results = await _serverContext.SendCommand(cmd);
    }
}