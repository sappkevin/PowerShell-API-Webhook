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
                
                // Debug output of current environment
                var currentDirectory = Directory.GetCurrentDirectory();
                _logger.LogInformation($"Current directory: {currentDirectory}");
                _logger.LogInformation($"Requested script: {scriptToCheck.Script}");
                
                // Log available directories
                try
                {
                    var dirs = Directory.GetDirectories(currentDirectory);
                    _logger.LogInformation($"Available directories: {string.Join(", ", dirs)}");
                    
                    if (Directory.Exists(Path.Combine(currentDirectory, "scripts")))
                    {
                        var scriptDirs = Directory.GetDirectories(Path.Combine(currentDirectory, "scripts"));
                        _logger.LogInformation($"Script subdirectories: {string.Join(", ", scriptDirs)}");
                        
                        if (Directory.Exists(Path.Combine(currentDirectory, "scripts", "powershell")))
                        {
                            var psFiles = Directory.GetFiles(Path.Combine(currentDirectory, "scripts", "powershell"));
                            _logger.LogInformation($"PowerShell scripts: {string.Join(", ", psFiles)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error listing directories: {ex.Message}");
                }
                
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
                
                // Let's be very explicit about path resolution
                string scriptPath;
                
                // First check for absolute path in handler config
                if (Path.IsPathRooted(handler.ScriptsLocation))
                {
                    scriptPath = Path.Combine(handler.ScriptsLocation, cleanScriptName);
                    _logger.LogInformation($"Using absolute path from config: {scriptPath}");
                }
                // If it's a relative path starting with ./ or ../
                else if (handler.ScriptsLocation.StartsWith("./") || handler.ScriptsLocation.StartsWith("../"))
                {
                    scriptPath = Path.GetFullPath(Path.Combine(currentDirectory, handler.ScriptsLocation, cleanScriptName));
                    _logger.LogInformation($"Using relative path from config: {scriptPath}");
                }
                // Try a direct path under the current directory
                else
                {
                    // If path does not contain directory separators, assume a subdirectory of current directory
                    if (!handler.ScriptsLocation.Contains(Path.DirectorySeparatorChar))
                    {
                        scriptPath = Path.Combine(currentDirectory, handler.ScriptsLocation, cleanScriptName);
                        _logger.LogInformation($"Using path as subdirectory: {scriptPath}");
                    }
                    else
                    {
                        // Otherwise use exactly as specified
                        scriptPath = Path.Combine(handler.ScriptsLocation, cleanScriptName);
                        _logger.LogInformation($"Using path as specified: {scriptPath}");
                    }
                }
                
                // Explicit fallback for CI environment - if nothing else worked, try a direct path under scripts/powershell
                if (!File.Exists(scriptPath))
                {
                    var fallbackPath = Path.Combine(currentDirectory, "scripts", "powershell", cleanScriptName);
                    _logger.LogInformation($"Script not found, trying fallback path: {fallbackPath}");
                    
                    if (File.Exists(fallbackPath))
                    {
                        scriptPath = fallbackPath;
                        _logger.LogInformation($"Using fallback path: {scriptPath}");
                    }
                }
                
                // Set the path on the script object
                scriptToCheck.ScriptPath = scriptPath;
                _logger.LogInformation($"Final script path: {scriptToCheck.ScriptPath}");
                
                // Verify the script exists
                if (!File.Exists(scriptToCheck.ScriptPath))
                {
                    _logger.LogWarning($"Script file not found: {scriptToCheck.ScriptPath}");
                    
                    result.Errors.Add($"Script '{scriptToCheck.Script}' not found at path '{scriptToCheck.ScriptPath}'. Please verify the script name and location.");
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