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
    /// Controller for handling webhook requests to execute PowerShell scripts with intelligent script detection
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
        /// Executes a script via POST request with intelligent script detection
        /// </summary>
        /// <param name="requestBody">Script execution request - can be DtoScript or flexible JSON</param>
        /// <param name="key">Security key from query string (optional if in body)</param>
        /// <param name="script">Script name from query string (optional - will auto-detect if not provided)</param>
        /// <remarks>
        /// Sample requests:
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "fqdn": "test-device.linkedin.biz",
        ///        "ipv4Addr": "172.30.29.23"
        ///     }
        ///     # Auto-detects webhookshell.ps1 (IPv4 only)
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "fqdn": "test-device.linkedin.biz",
        ///        "ipv6Addr": "2001:db8::1",
        ///        "event": "Connected"
        ///     }
        ///     # Auto-detects webhookshell.ps1 (IPv6 only)
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "fqdn": "test-device.linkedin.biz",
        ///        "event": "test-connected"
        ///     }
        ///     # Auto-detects webhookshell.ps1 (minimal params for testing)
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "fqdn": "test-device.linkedin.biz",
        ///        "ipv4Addr": "172.30.29.23",
        ///        "ipv6Addr": "2001:db8::1",
        ///        "event": "Connected",
        ///        "nameSrv1": "lva1-adc01.linkedin.biz",
        ///        "serialNumber": "C02ZQ406MD6R"
        ///     }
        ///     # Auto-detects webhookshell.ps1 (full parameters)
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "names": "test-dns.corp.linkedin.com",
        ///        "command": "check"
        ///     }
        ///     # Auto-detects condForwarderAPI.ps1 (required params)
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "names": "app.prod.linkedin.com,api.grid.linkedin.com",
        ///        "command": "add",
        ///        "DryRun": "true"
        ///     }
        ///     # Auto-detects condForwarderAPI.ps1 (with optional dry run)
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "Component": "system"
        ///     }
        ///     # Auto-detects health-check-script.ps1 (minimal health check)
        /// 
        ///     POST /webhook/v1?key=24ffc5be-7dd8-479f-898e-27169bf23e7f
        ///     {
        ///        "RetentionDays": "30"
        ///     }
        ///     # Auto-detects daily-cleanup.ps1 (minimal cleanup)
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
                    // Flexible format - detect script based on parameters
                    var detectedScript = script ?? DetectScriptFromParameters(requestBody);
                    var apiKey = key ?? string.Empty;

                    if (string.IsNullOrEmpty(detectedScript))
                    {
                        return BadRequest(new[] { "Unable to determine which script to execute based on the provided parameters. Please specify 'script' parameter or ensure parameters match a configured script pattern." });
                    }

                    // Build parameters from JSON body
                    var parameters = BuildParametersFromRequest(requestBody);

                    scriptToExecute = new DtoScript
                    {
                        Script = detectedScript,
                        Key = apiKey,
                        Parameters = parameters
                    };

                    _logger.LogInformation($"Auto-detected script: {detectedScript}, Parameters: {parameters}");
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
        /// Detects which script to use based on the parameters in the request
        /// </summary>
        private string DetectScriptFromParameters(Dictionary<string, object> parameters)
        {
            var paramKeys = parameters.Keys.Select(k => k.ToLowerInvariant()).ToHashSet();

            // Define parameter patterns for each script with required and optional parameters
            var scriptPatterns = new Dictionary<string, ScriptPattern>
            {
                // DNS Record Management - requires at least fqdn + (ipv4addr OR ipv6addr OR event)
                ["webhookshell.ps1"] = new ScriptPattern
                {
                    RequiredParams = new string[] { "fqdn" },
                    OptionalParams = new string[] { "ipv4addr", "ipv6addr", "event", "namesrv1", "namesrv2", "serialnumber" },
                    MinimumMatches = 2 // fqdn + at least one other param
                },
                
                // DNS Forwarder API - requires both names and command
                ["condForwarderAPI.ps1"] = new ScriptPattern
                {
                    RequiredParams = new string[] { "names", "command" },
                    OptionalParams = new string[] { "a", "stage", "dryrun" },
                    MinimumMatches = 2 // both names and command required
                },
                
                // Health Check - requires at least one health-related parameter
                ["health-check-script.ps1"] = new ScriptPattern
                {
                    RequiredParams = new string[0],
                    OptionalParams = new string[] { "component", "outputformat", "detailed", "format" },
                    MinimumMatches = 1 // any health check param
                },
                
                // Daily Cleanup - requires at least one cleanup-related parameter
                ["daily-cleanup.ps1"] = new ScriptPattern
                {
                    RequiredParams = new string[0],
                    OptionalParams = new string[] { "retentiondays", "loglevel", "includepaths", "excludepaths", "dryrun" },
                    MinimumMatches = 1 // any cleanup param
                }
            };

            // Find the script with the best match
            string bestMatch = null;
            int highestScore = 0;
            bool hasRequiredParams = false;

            foreach (var scriptPattern in scriptPatterns)
            {
                var scriptName = scriptPattern.Key;
                var pattern = scriptPattern.Value;
                
                // Check if all required parameters are present
                bool allRequiredPresent = pattern.RequiredParams.All(param => paramKeys.Contains(param));
                
                // Calculate total match score (required + optional parameters)
                int requiredMatches = pattern.RequiredParams.Count(param => paramKeys.Contains(param));
                int optionalMatches = pattern.OptionalParams.Count(param => paramKeys.Contains(param));
                int totalMatches = requiredMatches + optionalMatches;
                
                // Calculate weighted score (required params worth more)
                int weightedScore = (requiredMatches * 3) + optionalMatches;
                
                // Check if this pattern meets the minimum criteria
                bool meetsMinimum = totalMatches >= pattern.MinimumMatches && allRequiredPresent;
                
                _logger.LogDebug($"Script '{scriptName}': Required={requiredMatches}/{pattern.RequiredParams.Length}, " +
                               $"Optional={optionalMatches}/{pattern.OptionalParams.Length}, " +
                               $"Total={totalMatches}, WeightedScore={weightedScore}, " +
                               $"MeetsMinimum={meetsMinimum}");
                
                // Prefer scripts that meet minimum requirements and have higher scores
                if (meetsMinimum && (weightedScore > highestScore || 
                    (weightedScore == highestScore && allRequiredPresent && !hasRequiredParams)))
                {
                    highestScore = weightedScore;
                    bestMatch = scriptName;
                    hasRequiredParams = allRequiredPresent;
                }
            }

            // Log the detection logic
            if (!string.IsNullOrEmpty(bestMatch))
            {
                _logger.LogInformation($"Script detection: '{bestMatch}' matched with score {highestScore} for parameters: {string.Join(", ", paramKeys)}");
            }
            else
            {
                _logger.LogWarning($"Script detection: No script matched for parameters: {string.Join(", ", paramKeys)}");
                // Fallback to first script in configuration
                bestMatch = GetDefaultScriptName();
                _logger.LogInformation($"Using fallback script: {bestMatch}");
            }

            return bestMatch;
        }

        /// <summary>
        /// Defines the pattern for script parameter matching
        /// </summary>
        private class ScriptPattern
        {
            public string[] RequiredParams { get; set; } = new string[0];
            public string[] OptionalParams { get; set; } = new string[0];
            public int MinimumMatches { get; set; } = 1;
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
