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
                
                // Set the full script path for execution, ensuring it's a clean path
                string cleanScriptName = Path.GetFileName(scriptToCheck.Script);
                
                if (Path.IsPathRooted(handler.ScriptsLocation))
                {
                    // If the handler has an absolute path, use it directly
                    scriptToCheck.ScriptPath = Path.Combine(handler.ScriptsLocation, cleanScriptName);
                }
                else
                {
                    // If it's a relative path, make it relative to the current directory
                    string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    string relativePath = handler.ScriptsLocation.TrimStart('.', '/', '\\');
                    scriptToCheck.ScriptPath = Path.Combine(baseDirectory, relativePath, cleanScriptName);
                }
                
                _logger.LogInformation($"Full script path: {scriptToCheck.ScriptPath}");
                
                // Verify the script exists
                if (!File.Exists(scriptToCheck.ScriptPath))
                {
                    _logger.LogWarning($"Script file not found: {scriptToCheck.ScriptPath}");
                    
                    // Try to find the script by searching the directory
                    string scriptDirectory = Path.GetDirectoryName(scriptToCheck.ScriptPath);
                    
                    if (Directory.Exists(scriptDirectory))
                    {
                        _logger.LogInformation($"Directory exists: {scriptDirectory}");
                        _logger.LogInformation($"Directory contents: {string.Join(", ", Directory.GetFiles(scriptDirectory))}");
                    }
                    else
                    {
                        _logger.LogWarning($"Directory does not exist: {scriptDirectory}");
                    }
                    
                    result.Errors.Add($"Script '{scriptToCheck.Script}' not found in the scripts directory. Please verify the script name and try again.");
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