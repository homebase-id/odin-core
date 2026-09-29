using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Registry;
using Odin.Services.Registry.PayloadMove;
using Odin.Services.Tenant.Container;

#nullable enable

namespace Odin.Hosting.Controllers.PayloadMove;

/// <summary>
/// The source side of a payload move: serves one moved identity's payloads and thumbnails to the host that
/// imported it. Objects are addressed by what they are, and the path is built here under that identity's
/// root, so no client-supplied path is ever used. Every refusal is a 404.
/// See docs/superpowers/specs/2026-08-31-payload-migration-design.md.
/// </summary>
[ApiController]
[Route(RootPath + "/v1/{identityId:guid}")]
[ServiceFilter(typeof(PayloadMoveRestrictedAttribute))]
[ApiExplorerSettings(IgnoreApi = true)]
public class PayloadMoveController(
    IIdentityRegistry registry,
    IMultiTenantContainer tenants,
    PayloadMoveSource source,
    OdinConfiguration config,
    ILogger<PayloadMoveController> logger) : ControllerBase
{
    public const string RootPath = "/api/payload-move";

    // A storage provider that throttles us is passed on as a 429, so the target slows down instead of
    // taking the source for broken
    private const int ThrottledRetryAfterSeconds = 30;

    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem(Guid identityId, [FromBody] PayloadMoveRedeemRequest request)
    {
        if (ServableRegistration(identityId) == null)
        {
            return NotFound();
        }

        var credential = await source.RedeemAsync(identityId, request.HandoffToken ?? "");
        if (credential == null)
        {
            logger.LogWarning("Refused to redeem a payload move handoff token for {identityId}", identityId);
            return NotFound();
        }

        logger.LogInformation("Payload move for {identityId} started: handoff token redeemed", identityId);
        return Ok(new PayloadMoveRedeemResponse { Credential = credential });
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete(Guid identityId)
    {
        if (!await source.CompleteAsync(identityId, Credential()))
        {
            return NotFound();
        }

        logger.LogInformation("Payload move for {identityId} completed: the target has every payload", identityId);
        return Ok();
    }

    [HttpGet("payload/{driveId:guid}/{fileId:guid}/{key}/{uid:long}")]
    [HttpHead("payload/{driveId:guid}/{fileId:guid}/{key}/{uid:long}")]
    public Task<IActionResult> GetPayload(Guid identityId, Guid driveId, Guid fileId, string key, long uid)
    {
        return ServeAsync(identityId, key,
            paths => paths.GetPayloadDirectoryAndFileName(driveId, fileId, key, new UnixTimeUtcUnique(uid)));
    }

    [HttpGet("thumb/{driveId:guid}/{fileId:guid}/{key}/{uid:long}/{width:int}x{height:int}")]
    [HttpHead("thumb/{driveId:guid}/{fileId:guid}/{key}/{uid:long}/{width:int}x{height:int}")]
    public Task<IActionResult> GetThumbnail(Guid identityId, Guid driveId, Guid fileId, string key, long uid, int width, int height)
    {
        return ServeAsync(identityId, key,
            paths => paths.GetThumbnailDirectoryAndFileName(driveId, fileId, key, new UnixTimeUtcUnique(uid), width, height));
    }

    //

    private async Task<IActionResult> ServeAsync(Guid identityId, string key, Func<TenantPathManager, string> pathOf)
    {
        var registration = ServableRegistration(identityId);
        if (registration == null || !TenantPathManager.IsValidPayloadKey(key) || !await source.AuthorizeAsync(identityId, Credential()))
        {
            return NotFound();
        }

        await using var scope = tenants.GetTenantScope(registration.PrimaryDomainName).BeginLifetimeScope("PayloadMove");
        var store = scope.Resolve<LongTermPayloadStore>();
        var path = pathOf(new TenantPathManager(config, identityId));

        try
        {
            if (HttpMethods.IsHead(Request.Method))
            {
                if (!await store.ExistsAsync(path, HttpContext.RequestAborted))
                {
                    return NotFound();
                }

                Response.ContentLength = await store.LengthAsync(path, HttpContext.RequestAborted);
                return Ok();
            }

            var stream = await store.OpenReadAsync(path, HttpContext.RequestAborted);
            Response.ContentLength = stream.Length;
            return File(stream, "application/octet-stream");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (IsThrottled(e))
            {
                Response.Headers.RetryAfter = ThrottledRetryAfterSeconds.ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests);
            }

            if (!await store.ExistsAsync(path, HttpContext.RequestAborted))
            {
                return NotFound();
            }

            throw;
        }
    }

    // Only an identity this host holds and has stopped: if someone resumed it, its data is live here again
    // and must not flow into a second copy
    private IdentityRegistration? ServableRegistration(Guid identityId)
    {
        var registration = registry.Get(identityId);
        return registration != null && !TenantStatusRules.RunsBackgroundServices(registration.Status) ? registration : null;
    }

    private string? Credential()
    {
        var header = Request.Headers.Authorization.FirstOrDefault();
        return header != null && header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..] : null;
    }

    private static bool IsThrottled(Exception e)
    {
        for (var inner = e; inner != null; inner = inner.InnerException)
        {
            if (inner is AmazonS3Exception { StatusCode: HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests })
            {
                return true;
            }
        }

        return false;
    }
}
