using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using WaitingListApi.Data;
using Youverse.Core.Exceptions.Server;

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

        try
        {
            _storage.Insert(info);
        }
        catch (SqliteException sqliteException)
        {
            if (sqliteException.SqliteErrorCode == 19)
            {
                return BadRequest("");
            }
        }
        catch
        {
            return Problem(statusCode: 500);
        }

        return Ok();
    }
}