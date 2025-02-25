using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using Webhookshell.Interfaces;
using Webhookshell.Models;
using Webhookshell.Options;
using Microsoft.Extensions.Logging;

namespace Webhookshell.Services
{
    public class HandlerDispatcher : IHandlerDispatcher
    {
        private readonly ScriptOptions _options;
        private readonly ILogger<HandlerDispatcher> _logger;
        
        public HandlerDispatcher(IOptionsSnapshot<ScriptOptions> options, ILogger<HandlerDispatcher> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public Result<ScriptHandler> GetScriptHandler(DtoScript scriptToCheck)
        {
            Result<ScriptHandler> result = new();
            
            try
            {
                if (string.IsNullOrEmpty(scriptToCheck.Script))
                {
                    result.Errors.Add("Script name cannot be empty.");
                    return result;
                }
                
                _logger.LogInformation($"Processing script request: {scriptToCheck.Script}");
                
                // Fixed bug: Properly extract extension by getting the part after the last dot
                string scriptExtension = null;
                int lastDotIndex = scriptToCheck.Script.LastIndexOf('.');
                
                if (lastDotIndex > 0 && lastDotIndex < scriptToCheck.Script.Length - 1)
                {
                    scriptExtension = scriptToCheck.Script.Substring(lastDotIndex + 1);
                }

                if (string.IsNullOrEmpty(scriptExtension))
                {
                    result.Errors.Add($"Unable to extract script extension from '{scriptToCheck.Script}'. Script name must include a file extension (e.g., .ps1, .py).");
                    return result;
                }

                _logger.LogInformation($"Script extension: {scriptExtension}");
                
                // Log the available handlers for debugging
                _logger.LogInformation($"Available handlers: {string.Join(", ", _options.Handlers.Select(h => h.FileExtension))}");

                ScriptHandler handler = _options
                    .Handlers
                    .Where(script => string.Equals(script.FileExtension, scriptExtension, StringComparison.InvariantCultureIgnoreCase))
                    .FirstOrDefault();

                if (handler is null)
                {
                    result.Errors.Add($"Unable to find a handler for the script extension '{scriptExtension}'. You need to add the handler to the service config.");
                    return result;
                }

                _logger.LogInformation($"Found handler for {scriptExtension}, script location: {handler.ScriptsLocation}");
                
                // Set the full script path for execution
                string scriptName = Path.GetFileName(scriptToCheck.Script);
                
                // Use absolute path handling with multiple fallbacks
                string scriptPath = null;
                
                // Try handler location directly first
                scriptPath = Path.Combine(handler.ScriptsLocation, scriptName);
                _logger.LogInformation($"Trying script path: {scriptPath}");
                
                if (!File.Exists(scriptPath))
                {
                    // Try resolving as an absolute path if handler location is absolute
                    if (Path.IsPathRooted(handler.ScriptsLocation))
                    {
                        scriptPath = Path.Combine(handler.ScriptsLocation, scriptName);
                        _logger.LogInformation($"Trying absolute path: {scriptPath}");
                    }
                    else
                    {
                        // Try as relative to application directory
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        scriptPath = Path.Combine(baseDir, handler.ScriptsLocation, scriptName);
                        _logger.LogInformation($"Trying relative to app directory: {scriptPath}");
                        
                        // If still not found, try removing any ./ prefix
                        if (!File.Exists(scriptPath) && handler.ScriptsLocation.StartsWith("./"))
                        {
                            string noRelativePath = handler.ScriptsLocation.Substring(2);
                            scriptPath = Path.Combine(baseDir, noRelativePath, scriptName);
                            _logger.LogInformation($"Trying without ./ prefix: {scriptPath}");
                        }
                        
                        // Last attempt - try at app root level directly
                        if (!File.Exists(scriptPath))
                        {
                            scriptPath = Path.Combine(baseDir, "scripts", "powershell", scriptName);
                            _logger.LogInformation($"Last attempt at app root: {scriptPath}");
                        }
                    }
                }
                
                // Set the path on the script object
                scriptToCheck.ScriptPath = scriptPath;
                _logger.LogInformation($"Final script path: {scriptToCheck.ScriptPath}");
                
                // Verify the script exists
                if (!File.Exists(scriptToCheck.ScriptPath))
                {
                    _logger.LogWarning($"Script file not found: {scriptToCheck.ScriptPath}");
                    result.Errors.Add($"Script '{scriptToCheck.Script}' not found at path '{scriptToCheck.ScriptPath}'. Please verify the script exists.");
                    return result;
                }

                result.Success = true;
                result.Data = handler;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting script handler for script: {scriptToCheck.Script}");
                result.Errors.Add($"An error occurred while processing the script: {ex.Message}");
                return result;
            }
        }
    }
}