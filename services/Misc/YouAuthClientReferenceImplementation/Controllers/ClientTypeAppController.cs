using YouAuthClientReferenceImplementation.Models;
using Microsoft.AspNetCore.Mvc;

namespace YouAuthClientReferenceImplementation.Controllers;

public class ClientTypeAppController : BaseController
{
    public string LoggedInIdentity => Request.Cookies["OdinAppIdentity"] ?? "";
    public string OdinCat => Request.Cookies["OdinAppCat"] ?? "";

    private readonly ILogger<ClientTypeAppController> _logger;

    public ClientTypeAppController(ILogger<ClientTypeAppController> logger) : base(logger)
    {
        _logger = logger;
    }

    //

    // GET
    public IActionResult Index()
    {
        // if (LoggedInIdentity == "")
        // {
        //     return View(new ClientTypeDomainIndexViewModel
        //     {
        //         LoggedInMessage = "Not logged in",
        //         ButtonCaption = "Log in"
        //     });
        // }
        //
        // return View(new ClientTypeDomainIndexViewModel
        // {
        //     LoggedInMessage = $"Logged in as {LoggedInIdentity}",
        //     ButtonCaption = "Log out",
        // });
        ShowError("Nothing here yet");
        return View();
    }
}