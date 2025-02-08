using Microsoft.AspNetCore.Mvc;
using Webhookshell.Interfaces;
using Webhookshell.Models;
using System.Threading.Tasks;

namespace Webhookshell.Controllers
{
    [ApiController]
    [Route("[controller]/v1/")] // Defines the base route for this API controller
    public class WebHookController : ControllerBase
    {
        private readonly IScriptRunnerService _scriptRunner; // Dependency injection for script execution service

        // Constructor initializes the script runner service via dependency injection
        public WebHookController(IScriptRunnerService scriptRunner)
        {
            _scriptRunner = scriptRunner;
        }

        // Handles GET requests to start a script using query parameters
        [HttpGet]
        public async Task<IActionResult> StartScriptFromGet([FromQuery] DtoScript scriptFromQuery)
        {
            var scriptRun = await _scriptRunner.RunAsync(scriptFromQuery, HttpContext); // Asynchronously run the script

            if (scriptRun.IsValid)
            {
                return Ok(scriptRun.Data); // Return 200 OK with script output if successful
            }

            return BadRequest(scriptRun.Errors); // Return 400 Bad Request if validation fails
        }

        // Handles POST requests to start a script using the request body
        [HttpPost]
        public async Task<IActionResult> StartScriptFromPost([FromBody] DtoScript scriptFromBody)
        {
            var scriptRun = await _scriptRunner.RunAsync(scriptFromBody, HttpContext); // Asynchronously run the script

            if (scriptRun.IsValid)
            {
                return Ok(scriptRun.Data); // Return 200 OK with script output if successful
            }

            return BadRequest(scriptRun.Errors); // Return 400 Bad Request if validation fails
        }
    }
}
