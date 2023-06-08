using System.Threading.Tasks;

namespace Youverse.Core.Services.Email;

public interface IEmailSender
{
    Task SendAsync(Envelope envelope);
}