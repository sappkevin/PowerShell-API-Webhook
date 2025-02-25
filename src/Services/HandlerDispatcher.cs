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
                
                // Extract extension by getting the part after the last dot
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

                // Find the appropriate handler for this script extension
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
                
                // Extract script filename (removing any path components that might have been included)
                string scriptName = Path.GetFileName(scriptToCheck.Script);
                _logger.LogInformation($"Script name: {scriptName}");
                
                // If scriptPath was explicitly provided, use it directly after validation
                if (!string.IsNullOrEmpty(scriptToCheck.ScriptPath) && File.Exists(scriptToCheck.ScriptPath))
                {
                    _logger.LogInformation($"Using provided script path: {scriptToCheck.ScriptPath}");
                    
                    // But still check that the script name matches
                    if (!Path.GetFileName(scriptToCheck.ScriptPath).Equals(scriptName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        result.Errors.Add($"Script name mismatch: Script parameter is '{scriptName}' but provided path contains '{Path.GetFileName(scriptToCheck.ScriptPath)}'");
                        return result;
                    }
                }
                else
                {
                    // Path resolution strategy - try multiple approaches
                    string[] pathsToTry = GetPossibleScriptPaths(handler.ScriptsLocation, scriptName);
                    
                    // Log all the paths we're going to try
                    _logger.LogInformation($"Will try the following paths to locate the script:");
                    foreach (var path in pathsToTry)
                    {
                        _logger.LogInformation($"- {path}");
                    }
                    
                    // Try each path in order until we find one that exists
                    string foundPath = pathsToTry.FirstOrDefault(File.Exists);
                    
                    if (string.IsNullOrEmpty(foundPath))
                    {
                        _logger.LogWarning($"Script file not found in any of the attempted paths");
                        result.Errors.Add($"Script '{scriptToCheck.Script}' not found. Please verify the script exists.");
                        return result;
                    }
                    
                    // Set the resolved path
                    scriptToCheck.ScriptPath = foundPath;
                    _logger.LogInformation($"Successfully resolved script path: {scriptToCheck.ScriptPath}");
                }
                
                // Double-check that the script exists at the resolved path
                if (!File.Exists(scriptToCheck.ScriptPath))
                {
                    _logger.LogWarning($"Script file not found at resolved path: {scriptToCheck.ScriptPath}");
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

        /// <summary>
        /// Creates an array of possible script paths to try in order of most likely to work
        /// </summary>
        private string[] GetPossibleScriptPaths(string handlerScriptsLocation, string scriptName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Directory.GetCurrentDirectory();
            
            // Generate multiple possible paths to try in sequence
            return new[]
            {
                // 1. If handler location is absolute, try it directly
                Path.IsPathRooted(handlerScriptsLocation) 
                    ? Path.Combine(handlerScriptsLocation, scriptName) 
                    : null,
                
                // 2. Relative to the specified scripts location
                Path.Combine(handlerScriptsLocation, scriptName),
                
                // 3. Relative to application base directory
                Path.Combine(baseDir, handlerScriptsLocation, scriptName),
                
                // 4. Without "./" prefix if it exists
                handlerScriptsLocation.StartsWith("./")
                    ? Path.Combine(baseDir, handlerScriptsLocation.Substring(2), scriptName)
                    : null,
                
                // 5. Relative to current directory
                Path.Combine(currentDir, handlerScriptsLocation, scriptName),
                
                // 6. Standard path relative to application for container environments
                Path.Combine(baseDir, "scripts", Path.GetExtension(scriptName)?.TrimStart('.') ?? "powershell", scriptName),
                
                // 7. Fallback for Docker/container standard paths
                Path.Combine("/app/scripts", Path.GetExtension(scriptName)?.TrimStart('.') ?? "powershell", scriptName)
            }
            .Where(path => !string.IsNullOrEmpty(path)) // Filter out any null entries
            .Distinct() // Remove duplicates
            .ToArray();
        }
    }
}
