using System.Threading.Tasks;

namespace Odin.Core.Services.Email;

public interface IEmailSender
{
    Task SendAsync(Envelope envelope);
}