using System.Threading.Tasks;

namespace Odin.Services.Email;

public interface IEmailSender
{
    Task SendAsync(Envelope envelope);
}