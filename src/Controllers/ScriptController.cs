using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Webhookshell.Interfaces;
using Webhookshell.Models;
using Webhookshell.Validators;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System; // Added for Exception class

namespace Webhookshell.Controllers
{
    /// <summary>
    /// Controller for executing parameterized scripts with flexible parameter passing
    /// </summary>
    [ApiController]
    [Route("[controller]/v1/")]
    [Produces("application/json")]
    public class ScriptController : ControllerBase
    {
        private readonly IScriptRunnerService _scriptRunner;
        private readonly IScriptValidationService _validationService;
        private readonly DtoScriptValidator _scriptValidator;
        private readonly ILogger<ScriptController> _logger;
        
        public ScriptController(
            IScriptRunnerService scriptRunner,
            IScriptValidationService validationService,
            DtoScriptValidator scriptValidator,
            ILogger<ScriptController> logger)
        {
            _scriptRunner = scriptRunner;
            _validationService = validationService;
            _scriptValidator = scriptValidator;
            _logger = logger;
        }

        /// <summary>
        /// Executes a script with parameters from request body
        /// </summary>
        /// <param name="request">Script execution request with parameters</param>
        /// <param name="key">Security key from query string</param>
        /// <param name="script">Script name from query string (optional if specified in body)</param>
        /// <remarks>
        /// Sample request:
        /// 
        ///     POST /script/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f&amp;script=condForwarderAPI.ps1
        ///     {
        ///        "names": "test-dns.corp.linkedin.com test-dns2.corp.linkedin.com",
        ///        "command": "check"
        ///     }
        /// 
        /// Or with script in body:
        /// 
        ///     POST /script/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "script": "condForwarderAPI.ps1",
        ///        "names": "test-dns.corp.linkedin.com test-dns2.corp.linkedin.com",
        ///        "command": "check"
        ///     }
        /// 
        /// </remarks>
        /// <response code="200">Returns the script execution results</response>
        /// <response code="400">If script validation fails</response>
        [HttpPost]
        [ProducesResponseType(typeof(DtoResult), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string[]), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> ExecuteParameterizedScript(
            [FromBody] Dictionary<string, object> request,
            [FromQuery] string key,
            [FromQuery] string script = null)
        {
            try
            {
                // Validate required parameters
                if (string.IsNullOrEmpty(key))
                {
                    return BadRequest(new[] { "Key parameter is required in query string" });
                }

                if (request == null || !request.Any())
                {
                    return BadRequest(new[] { "Request body cannot be empty" });
                }

                // Determine script name - prioritize query parameter, then body
                string scriptName = script;
                if (string.IsNullOrEmpty(scriptName) && request.ContainsKey("script"))
                {
                    scriptName = request["script"]?.ToString();
                }

                if (string.IsNullOrEmpty(scriptName))
                {
                    return BadRequest(new[] { "Script name must be provided either in query string or request body" });
                }

                // Build parameters string from request body
                var parameters = BuildParametersFromRequest(request, scriptName);

                // Create DtoScript object for execution
                var dtoScript = new DtoScript
                {
                    Script = scriptName,
                    Key = key,
                    Parameters = parameters
                };

                // Validate the script request
                var validationErrors = _scriptValidator.ValidateInput(dtoScript);
                if (validationErrors.Any())
                {
                    return BadRequest(validationErrors);
                }

                // Validate via the validation service
                var validationResult = _validationService.Validate(dtoScript, HttpContext);
                if (validationResult.IsNotValid)
                {
                    return BadRequest(validationResult.Errors);
                }

                // Execute the script
                var scriptRun = await _scriptRunner.RunAsync(dtoScript, HttpContext);

                if (scriptRun.IsValid)
                {
                    return Ok(scriptRun.Data);
                }

                return BadRequest(scriptRun.Errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing parameterized script");
                return StatusCode(500, new[] { "An error occurred while executing the script" });
            }
        }

        /// <summary>
        /// Builds PowerShell parameters string from request dictionary
        /// </summary>
        private string BuildParametersFromRequest(Dictionary<string, object> request, string scriptName)
        {
            var parameterParts = new List<string>();

            foreach (var kvp in request)
            {
                // Skip the script parameter as it's handled separately
                if (kvp.Key.Equals("script", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = kvp.Value?.ToString();
                if (string.IsNullOrEmpty(value))
                    continue;

                // Handle different parameter formats based on script requirements
                if (scriptName.Equals("condForwarderAPI.ps1", StringComparison.OrdinalIgnoreCase))
                {
                    parameterParts.Add(FormatCondForwarderParameter(kvp.Key, value));
                }
                else
                {
                    // Generic parameter formatting for other scripts
                    parameterParts.Add(FormatGenericParameter(kvp.Key, value));
                }
            }

            var result = string.Join(" ", parameterParts);
            _logger.LogInformation($"Built parameters for {scriptName}: {result}");
            return result;
        }

        /// <summary>
        /// Formats parameters specifically for condForwarderAPI.ps1
        /// </summary>
        private string FormatCondForwarderParameter(string key, string value)
        {
            return key.ToLowerInvariant() switch
            {
                "names" => $"-names \"{value}\"",
                "command" => $"-command \"{value}\"",
                "a" => string.IsNullOrEmpty(value) || value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "" : "-a",
                "stage" => string.IsNullOrEmpty(value) || value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "" : "-stage",
                "dryrun" => string.IsNullOrEmpty(value) || value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "" : "-DryRun",
                _ => $"-{key} \"{value}\""
            };
        }

        /// <summary>
        /// Generic parameter formatting for other scripts
        /// </summary>
        private string FormatGenericParameter(string key, string value)
        {
            // Check if value looks like a boolean
            if (bool.TryParse(value, out bool boolValue))
            {
                return boolValue ? $"-{key}" : "";
            }

            // Check if value needs quotes (contains spaces)
            if (value.Contains(' '))
            {
                return $"-{key} \"{value}\"";
            }

            return $"-{key} {value}";
        }

        /// <summary>
        /// Health check endpoint for the script controller
        /// </summary>
        [HttpGet("health")]
        public IActionResult GetHealth()
        {
            return Ok(new { Status = "Healthy", Controller = "Script" });
        }
    }
}
