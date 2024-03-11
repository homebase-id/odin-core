using System.Threading.Tasks;
using Quartz;

namespace Odin.Services.JobManagement;

public interface IJobEvent
{
    Task Execute(IJobExecutionContext context, JobStatus status);
}
