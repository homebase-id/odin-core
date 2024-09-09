using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.JobManagement;

namespace Odin.Hosting.Controllers.Job;
#nullable enable

[ApiController]
public class JobController(IJobManager jobManager) : ControllerBase
{
    public const string GetJobResponseRouteName = "GetJobResponseRoute";

    //

    [AllowAnonymous]
    [HttpGet("/api/job/v1/{jobId}", Name = GetJobResponseRouteName)]
    public async Task<ActionResult<JobApiResponse>> GetJobResponse(string jobId)
    {
        var job = await jobManager.GetJobAsync<AbstractJob>(Guid.Parse(jobId));
        if (job == null)
        {
            return NotFound(job);
        }

        var result = job.CreateApiResponseObject();

        return Ok(result);
    }

    //

#if DEBUG
    [AllowAnonymous]
    [HttpGet("/api/job/v1/dummy")]
    public async Task<ActionResult> JobTest()
    {
        var job = jobManager.NewJob<DummyJob>();
        job.Data.Echo = "Hello, World!";

        var jobId = await jobManager.ScheduleJobAsync(job, JobSchedule.Now);

        return AcceptedAtRoute(GetJobResponseRouteName, new { jobId });
    }
#endif

}
