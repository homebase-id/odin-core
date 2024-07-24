using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.JobManagement;

namespace Odin.Hosting.Controllers.Job;
#nullable enable

[ApiController]
public class JobController : ControllerBase
{
    public const string GetJobResponseRouteName = "GetJobResponseRoute";

    private readonly IJobManager _jobManager;

    public JobController(IJobManager jobManager)
    {
        _jobManager = jobManager;
    }

    //

    [AllowAnonymous]
    [HttpGet("/api/job/v1/{jobKey}", Name = GetJobResponseRouteName)]
    public async Task<ActionResult<OldJobResponse>> GetJobResponse(string jobKey)
    {
        var jk = OldHelpers.ParseJobKey(jobKey);
        var job = await _jobManager.GetResponse(jk);

        if (job.Status == OldJobStatus.NotFound)
        {
            return NotFound(job);
        }

        return Ok(job);
    }

    //

#if DEBUG
    [AllowAnonymous]
    [HttpGet("/api/job/v1/dummy")]
    public async Task<ActionResult> JobTest()
    {
        var scheduler = new DummySchedule("Hello, World!");
        var jobKey = await _jobManager.Schedule<DummyJob>(scheduler);
        return AcceptedAtRoute(GetJobResponseRouteName, new { jobKey });
    }
#endif

}
