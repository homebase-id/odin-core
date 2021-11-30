using System;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Exceptions.Client;
using Youverse.Core.Exceptions.Server;

namespace Youverse.Hosting.Controllers.Test
{
    public class ExceptionTestController : Controller
    {
        [HttpGet("/exception-test-api-server")]
        public IActionResult ApiTestServer()
        {
            throw new ServerException("Oh no!");
        }

        [HttpGet("/exception-test-api-client")]
        public IActionResult ApiTestClient()
        {
            throw new NotFoundException("Oh no!");
        }

        [HttpGet("/exception-test-api-generic")]
        public IActionResult ApiTestGeneric()
        {
            throw new Exception("Oh no!");
        }
    }
}
