using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Serilog;
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

        try
        {
            _storage.Insert(info);
            Log.Information("Added email address to the list");
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