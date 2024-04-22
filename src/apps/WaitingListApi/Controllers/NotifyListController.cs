using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using WaitingListApi.Data;

namespace WaitingListApi.Controllers;

[ApiController]
[Route("api/notify")]
public class NotifyListController(WaitingListStorage storage, ILogger<NotifyListController> logger) : ControllerBase
{
    [HttpPost("add")]
    public IActionResult AddNotificationInfo([FromBody] NotificationInfo info)
    {
        if (string.IsNullOrEmpty(info.EmailAddress))
        {
            return BadRequest("Invalid or missing email address");
        }

        try
        {
            storage.Insert(info);
            logger.LogInformation("Added email address to the list");
        }
        catch (SqliteException sqliteException)
        {
            if (sqliteException.SqliteErrorCode == 19)
            {
                return BadRequest("Email address is already signed up");
            }
        }
        catch
        {
            return Problem(statusCode: 500);
        }

        return Ok();
    }
}
