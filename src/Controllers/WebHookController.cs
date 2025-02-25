using Microsoft.AspNetCore.Mvc;
using Webhookshell.Interfaces;
using Webhookshell.Models;
using Webhookshell.Validators;
using System.Threading.Tasks;
using System.Net;
using System.Linq;

namespace Webhookshell.Controllers
{
    /// <summary>
    /// Controller for handling webhook requests to execute PowerShell scripts
    /// </summary>
    [ApiController]
    [Route("[controller]/v1/")] // Defines the base route for this API controller
    [Produces("application/json")]
    public class WebHookController : ControllerBase
    {
        private readonly IScriptRunnerService _scriptRunner; // Dependency injection for script execution service
        private readonly DtoScriptValidator _scriptValidator; // Input validation service

        /// <summary>
        /// Initializes a new instance of the WebHookController
        /// </summary>
        /// <param name="scriptRunner">Script runner service</param>
        /// <param name="scriptValidator">Script input validator</param>
        public WebHookController(IScriptRunnerService scriptRunner, DtoScriptValidator scriptValidator)
        {
            _scriptRunner = scriptRunner;
            _scriptValidator = scriptValidator;
        }

        /// <summary>
        /// Executes a script via GET request
        /// </summary>
        /// <param name="scriptFromQuery">Script execution details</param>
        /// <remarks>
        /// Sample request:
        /// 
        ///     GET /webhook/v1?script=Test-Script.ps1&amp;key=24ffc5be-7dd8-479f-898e-27169bf23e7f&amp;parameters=-Param1 test -Param2 sample
        /// 
        /// </remarks>
        /// <response code="200">Returns the script execution results</response>
        /// <response code="400">If script validation fails</response>
        [HttpGet]
        [ProducesResponseType(typeof(DtoResult), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string[]), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> StartScriptFromGet([FromQuery] DtoScript scriptFromQuery)
        {
            // Validate input parameters
            var validationErrors = _scriptValidator.ValidateInput(scriptFromQuery);
            if (validationErrors.Any())
            {
                return BadRequest(validationErrors);
            }

            var scriptRun = await _scriptRunner.RunAsync(scriptFromQuery, HttpContext); // Asynchronously run the script

            if (scriptRun.IsValid)
            {
                return Ok(scriptRun.Data); // Return 200 OK with script output if successful
            }

            return BadRequest(scriptRun.Errors); // Return 400 Bad Request if validation fails
        }

        /// <summary>
        /// Executes a script via POST request
        /// </summary>
        /// <param name="scriptFromBody">Script execution details</param>
        /// <remarks>
        /// Sample request:
        /// 
        ///     POST /webhook/v1
        ///     {
        ///        "script": "Test-Script.ps1",
        ///        "key": "24ffc5be-7dd8-479f-898e-27169bf23e7f",
        ///        "parameters": "-Param1 test -Param2 sample"
        ///     }
        /// 
        /// </remarks>
        /// <response code="200">Returns the script execution results</response>
        /// <response code="400">If script validation fails</response>
        [HttpPost]
        [ProducesResponseType(typeof(DtoResult), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string[]), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> StartScriptFromPost([FromBody] DtoScript scriptFromBody)
        {
            // Validate input parameters
            var validationErrors = _scriptValidator.ValidateInput(scriptFromBody);
            if (validationErrors.Any())
            {
                return BadRequest(validationErrors);
            }
            
            var scriptRun = await _scriptRunner.RunAsync(scriptFromBody, HttpContext); // Asynchronously run the script

            if (scriptRun.IsValid)
            {
                return Ok(scriptRun.Data); // Return 200 OK with script output if successful
            }

            return BadRequest(scriptRun.Errors); // Return 400 Bad Request if validation fails
        }
    }
}