using Microsoft.AspNetCore.Mvc;

namespace YouAuthClientReferenceImplementation.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public abstract class BaseController : Controller
{
    private readonly ILogger _logger;

    protected BaseController(ILogger logger)
    {
        _logger = logger;
    }

    protected void ShowError(string message)
    {
        TempData["ErrorMessage"] = message;
    }
}