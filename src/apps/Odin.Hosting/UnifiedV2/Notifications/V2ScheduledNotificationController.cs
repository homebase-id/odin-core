using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.AppNotifications.Push.Scheduled;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Notifications
{
    /// <summary>
    /// V2 endpoints for scheduling a (push) notification to be sent at a later time.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.Notify)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2ScheduledNotificationController(ScheduledNotificationService scheduledNotificationService)
        : OdinControllerBase
    {
        /// <summary>
        /// Schedules a notification to be enqueued/pushed at the requested time.
        /// </summary>
        [HttpPost("schedule")]
        public async Task<ScheduleNotificationResult> Schedule([FromBody] ScheduleNotificationRequest request)
        {
            var caller = WebOdinContext.GetCallerOdinIdOrFail();
            var jobId = await scheduledNotificationService.ScheduleNotificationAsync(
                caller, request.Options, request.SendAt, WebOdinContext);

            return new ScheduleNotificationResult { JobId = jobId };
        }

        /// <summary>
        /// Cancels a previously scheduled notification by its job id.
        /// </summary>
        [HttpDelete("schedule/{jobId:guid}")]
        public async Task<IActionResult> CancelSchedule(Guid jobId)
        {
            var cancelled = await scheduledNotificationService.CancelScheduledNotificationAsync(jobId, WebOdinContext);
            if (!cancelled)
            {
                return NotFound();
            }

            return Ok();
        }
    }
}
