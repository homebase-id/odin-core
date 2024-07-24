using System.Threading.Tasks;
using Quartz;

namespace Odin.Services.JobManagement;

public interface OldIJobEvent
{
    Task Execute(IJobExecutionContext context, OldJobStatus status);
}
