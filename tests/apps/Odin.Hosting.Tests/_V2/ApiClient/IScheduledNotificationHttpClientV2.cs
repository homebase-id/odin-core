using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.AppNotifications.Push.Scheduled;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

/// <summary>
/// Refit interface for the V2 scheduled (deferred) notification endpoints.
/// </summary>
public interface IScheduledNotificationHttpClientV2
{
    private const string Endpoint = UnifiedApiRouteConstants.Notify;

    [Post(Endpoint + "/schedule")]
    Task<ApiResponse<ScheduleNotificationResult>> Schedule([Body] ScheduleNotificationRequest request);

    [Put(Endpoint + "/schedule/{jobId}")]
    Task<ApiResponse<HttpContent>> Update(Guid jobId, [Body] ScheduleNotificationRequest request);

    [Delete(Endpoint + "/schedule/{jobId}")]
    Task<ApiResponse<HttpContent>> Cancel(Guid jobId);

    [Get(Endpoint + "/schedule")]
    Task<ApiResponse<List<ScheduledNotificationSummary>>> List();
}
