using Dawn;
using Microsoft.AspNetCore.Mvc;
using WaitingListApi.Data;

namespace WaitingListApi.Controllers;

[ApiController]
[Route("api/notify")]
public class NotifyListController : ControllerBase
{
    private readonly WaitingListStorage _storage;

    public NotifyListController(WaitingListStorage storage)
    {
        _storage = storage;
    }

    [HttpPost("add")]
    public IActionResult AddNotificationInfo([FromBody] NotificationInfo info)
    {
        if (string.IsNullOrEmpty(info.EmailAddress))
        {
            return BadRequest("Invalid or missing email address");
        }

        _storage.Insert(info);
        return Ok();
    }
}

public class NotificationInfo
{
    public string? EmailAddress { get; set; }
}