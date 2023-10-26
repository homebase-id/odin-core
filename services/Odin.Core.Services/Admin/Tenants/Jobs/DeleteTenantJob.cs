using System.Threading.Tasks;
using Odin.Core.Services.Registry;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;

public class DeleteTenantJob : IJob
{
    private readonly IIdentityRegistry _identityRegistry;

    public DeleteTenantJob(IIdentityRegistry identityRegistry)
    {
        _identityRegistry = identityRegistry;
    }

    //

    public async Task Execute(IJobExecutionContext context)
    {
        var domain = (string)context.JobDetail.JobDataMap["domain"];
        await _identityRegistry.DeleteRegistration(domain);
    }
}

