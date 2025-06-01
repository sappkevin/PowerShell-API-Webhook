using Microsoft.AspNetCore.Mvc;
using Webhookshell.Interfaces;
using Webhookshell.Models;
using Webhookshell.Validators;
using Webhookshell.Options;
using System.Threading.Tasks;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        private readonly ILogger<WebHookController> _logger;
        private readonly ScriptOptions _scriptOptions;

        /// <summary>
        /// Initializes a new instance of the WebHookController
        /// </summary>
        /// <param name="scriptRunner">Script runner service</param>
        /// <param name="scriptValidator">Script input validator</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="scriptOptions">Script configuration options</param>
        public WebHookController(
            IScriptRunnerService scriptRunner, 
            DtoScriptValidator scriptValidator, 
            ILogger<WebHookController> logger,
            IOptions<ScriptOptions> scriptOptions)
        {
            _scriptRunner = scriptRunner;
            _scriptValidator = scriptValidator;
            _logger = logger;
            _scriptOptions = scriptOptions.Value;
        }

        /// <summary>
        /// Executes a script via GET request
        /// </summary>
        /// <param name="scriptFromQuery">Script execution details</param>
        /// <param name="key">Security key from query string (optional if in body)</param>
        /// <param name="script">Script name from query string (optional if in body)</param>
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
        public async Task<IActionResult> StartScriptFromGet(
            [FromQuery] DtoScript scriptFromQuery, 
            [FromQuery] string key = null, 
            [FromQuery] string script = null)
        {
            // Fill in missing parameters from query string or configuration
            var enrichedScript = EnrichScriptWithDefaults(scriptFromQuery, key, script);

            // Validate input parameters
            var validationErrors = _scriptValidator.ValidateInput(enrichedScript);
            if (validationErrors.Any())
            {
                return BadRequest(validationErrors);
            }

            var scriptRun = await _scriptRunner.RunAsync(enrichedScript, HttpContext); // Asynchronously run the script

            if (scriptRun.IsValid)
            {
                return Ok(scriptRun.Data); // Return 200 OK with script output if successful
            }

            return BadRequest(scriptRun.Errors); // Return 400 Bad Request if validation fails
        }

        /// <summary>
        /// Executes a script via POST request with flexible parameter handling
        /// </summary>
        /// <param name="requestBody">Script execution request - can be DtoScript or flexible JSON</param>
        /// <param name="key">Security key from query string (optional if in body)</param>
        /// <param name="script">Script name from query string (optional if in body)</param>
        /// <remarks>
        /// Sample requests:
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "names": "test-dns.corp.linkedin.com test-dns2.corp.linkedin.com",
        ///        "command": "check"
        ///     }
        /// 
        /// Or traditional format:
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
        public async Task<IActionResult> StartScriptFromPost(
            [FromBody] Dictionary<string, object> requestBody, 
            [FromQuery] string key = null, 
            [FromQuery] string script = null)
        {
            try
            {
                DtoScript scriptToExecute;

                // Check if this is a traditional DtoScript format (has 'script' field)
                if (requestBody.ContainsKey("script") || requestBody.ContainsKey("key") || requestBody.ContainsKey("parameters"))
                {
                    // Traditional format - convert to DtoScript
                    scriptToExecute = new DtoScript
                    {
                        Script = requestBody.GetValueOrDefault("script")?.ToString() ?? script ?? string.Empty,
                        Key = requestBody.GetValueOrDefault("key")?.ToString() ?? key ?? string.Empty,
                        Parameters = requestBody.GetValueOrDefault("parameters")?.ToString() ?? string.Empty
                    };
                }
                else
                {
                    // Flexible format - build parameters from JSON body
                    var scriptName = script ?? GetDefaultScriptName();
                    var apiKey = key ?? string.Empty;

                    // Build parameters from JSON body for any script
                    var parameters = BuildParametersFromRequest(requestBody);

                    scriptToExecute = new DtoScript
                    {
                        Script = scriptName,
                        Key = apiKey,
                        Parameters = parameters
                    };

                    _logger.LogInformation($"Flexible format detected. Using script: {scriptName}, Parameters: {parameters}");
                }

                // Fill in missing parameters from configuration
                var enrichedScript = EnrichScriptWithDefaults(scriptToExecute, key, script);

                // Validate the script request
                var validationErrors = _scriptValidator.ValidateInput(enrichedScript);
                if (validationErrors.Any())
                {
                    return BadRequest(validationErrors);
                }

                var scriptRun = await _scriptRunner.RunAsync(enrichedScript, HttpContext); // Asynchronously run the script

                if (scriptRun.IsValid)
                {
                    return Ok(scriptRun.Data); // Return 200 OK with script output if successful
                }

                return BadRequest(scriptRun.Errors); // Return 400 Bad Request if validation fails
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook request");
                return StatusCode(500, new[] { "An error occurred while processing the request" });
            }
        }

        /// <summary>
        /// Enriches the script object with defaults from configuration
        /// </summary>
        private DtoScript EnrichScriptWithDefaults(DtoScript originalScript, string queryKey = null, string queryScript = null)
        {
            var enrichedScript = new DtoScript
            {
                Script = originalScript.Script,
                Key = originalScript.Key,
                Parameters = originalScript.Parameters,
                ScriptPath = originalScript.ScriptPath
            };

            // 1. Fill script from query parameter if missing
            if (string.IsNullOrEmpty(enrichedScript.Script) && !string.IsNullOrEmpty(queryScript))
            {
                enrichedScript.Script = queryScript;
                _logger.LogDebug($"Using script from query parameter: {queryScript}");
            }

            // 2. Fill key from query parameter if missing
            if (string.IsNullOrEmpty(enrichedScript.Key) && !string.IsNullOrEmpty(queryKey))
            {
                enrichedScript.Key = queryKey;
                _logger.LogDebug($"Using key from query parameter");
            }

            // 3. Fill script from configuration if still missing
            if (string.IsNullOrEmpty(enrichedScript.Script))
            {
                enrichedScript.Script = GetDefaultScriptName();
                _logger.LogDebug($"Using default script from configuration: {enrichedScript.Script}");
            }

            // 4. Fill key from configuration if still missing
            if (string.IsNullOrEmpty(enrichedScript.Key))
            {
                enrichedScript.Key = _scriptOptions.DefaultKey ?? string.Empty;
                _logger.LogDebug($"Using default key from configuration");
            }

            return enrichedScript;
        }

        /// <summary>
        /// Gets the default script name from configuration (first script found)
        /// </summary>
        private string GetDefaultScriptName()
        {
            // Find the first script in any handler
            var firstScript = _scriptOptions.Handlers?
                .SelectMany(h => h.ScriptsMapping ?? new List<ScriptMapping>())
                .FirstOrDefault()?.Name;

            return firstScript ?? string.Empty;
        }

        /// <summary>
        /// Builds PowerShell parameters string from request dictionary (generic approach)
        /// </summary>
        private string BuildParametersFromRequest(Dictionary<string, object> request)
        {
            var parameterParts = new List<string>();

            foreach (var kvp in request)
            {
                // Skip reserved fields
                if (IsReservedField(kvp.Key))
                    continue;

                var value = kvp.Value?.ToString();
                if (string.IsNullOrEmpty(value))
                    continue;

                // Generic parameter formatting
                parameterParts.Add(FormatGenericParameter(kvp.Key, value));
            }

            var result = string.Join(" ", parameterParts);
            return result;
        }

        /// <summary>
        /// Checks if a field name is reserved (script, key, parameters, etc.)
        /// </summary>
        private bool IsReservedField(string fieldName)
        {
            var reservedFields = new[] { "script", "key", "parameters", "scriptPath" };
            return reservedFields.Contains(fieldName.ToLowerInvariant());
        }

        /// <summary>
        /// Generic parameter formatting for any script
        /// </summary>
        private string FormatGenericParameter(string key, string value)
        {
            // Handle boolean-like values
            if (bool.TryParse(value, out bool boolValue))
            {
                return boolValue ? $"-{key}" : "";
            }

            // Handle switch parameters (common PowerShell pattern)
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || 
                value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return $"-{key}";
            }

            // Handle empty or false values for switches
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || 
                value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            // Handle values that need quotes (contain spaces or special characters)
            if (value.Contains(' ') || value.Contains('"') || value.Contains('\''))
            {
                // Escape any existing quotes
                var escapedValue = value.Replace("\"", "\\\"");
                return $"-{key} \"{escapedValue}\"";
            }

            // Simple parameter with value
            return $"-{key} {value}";
        }
    }
}
