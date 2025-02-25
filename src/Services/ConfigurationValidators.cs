using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Webhookshell.Options;

namespace Webhookshell.Services
{
    public class ConfigurationValidator
    {
        private readonly ILogger<ConfigurationValidator> _logger;
        private readonly ScriptOptions _options;

        public ConfigurationValidator(
            ILogger<ConfigurationValidator> logger,
            IOptionsMonitor<ScriptOptions> options)
        {
            _logger = logger;
            _options = options.CurrentValue;
        }

        public bool Validate()
        {
            try
            {
                _logger.LogInformation("Validating webhook configuration...");

                if (_options == null)
                {
                    _logger.LogError("Script options configuration is missing or invalid");
                    return false;
                }

                if (string.IsNullOrEmpty(_options.DefaultKey))
                {
                    _logger.LogWarning("DefaultKey is not configured. This may pose a security risk.");
                }

                if (_options.Handlers == null || !_options.Handlers.Any())
                {
                    _logger.LogError("No script handlers configured. API will not be able to execute any scripts.");
                    return false;
                }

                foreach (var handler in _options.Handlers)
                {
                    if (string.IsNullOrEmpty(handler.ScriptsLocation))
                    {
                        _logger.LogError($"Handler with ProcessName '{handler.ProcessName}' has no ScriptsLocation defined");
                        return false;
                    }

                    if (string.IsNullOrEmpty(handler.FileExtension))
                    {
                        _logger.LogError($"Handler with ProcessName '{handler.ProcessName}' has no FileExtension defined");
                        return false;
                    }

                    // Try to find the scripts directory using the same resolution logic
                    string resolvedScriptsDir = TryResolveScriptsDirectory(handler.ScriptsLocation);
                    if (resolvedScriptsDir == null)
                    {
                        _logger.LogWarning($"Scripts directory '{handler.ScriptsLocation}' for handler '{handler.ProcessName}' not found. Attempting to create it...");
                        
                        try
                        {
                            // Try multiple possible paths for creating the directory
                            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            string currentDir = Directory.GetCurrentDirectory();
                            
                            string[] dirsToTry = new[]
                            {
                                handler.ScriptsLocation, // As provided
                                Path.IsPathRooted(handler.ScriptsLocation) ? handler.ScriptsLocation : null, // If absolute
                                Path.Combine(baseDir, handler.ScriptsLocation), // Relative to app base
                                handler.ScriptsLocation.StartsWith("./") ? Path.Combine(baseDir, handler.ScriptsLocation.Substring(2)) : null, // Without ./
                                Path.Combine(baseDir, "scripts", handler.FileExtension), // Standard pattern
                                Path.Combine("/app/scripts", handler.FileExtension) // Docker standard
                            }.Where(d => !string.IsNullOrEmpty(d)).Distinct().ToArray();
                            
                            foreach (var dir in dirsToTry)
                            {
                                try
                                {
                                    _logger.LogInformation($"Attempting to create directory: {dir}");
                                    Directory.CreateDirectory(dir);
                                    _logger.LogInformation($"Successfully created directory: {dir}");
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Failed to create directory '{dir}': {ex.Message}");
                                    // Continue to next directory
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to create any scripts directory for '{handler.ScriptsLocation}'");
                            // We'll continue validation even if we couldn't create the directory
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Found scripts directory at: {resolvedScriptsDir}");
                    }

                    // Validate script mappings
                    if (handler.ScriptsMapping != null && handler.ScriptsMapping.Any())
                    {
                        foreach (var mapping in handler.ScriptsMapping)
                        {
                            if (string.IsNullOrEmpty(mapping.Name))
                            {
                                _logger.LogError($"Handler '{handler.ProcessName}' has a script mapping with no name");
                                return false;
                            }

                            // Check if script exists - using the resolved directory if available
                            string scriptDir = resolvedScriptsDir ?? handler.ScriptsLocation;
                            string scriptPath = Path.Combine(scriptDir, mapping.Name);
                            if (!File.Exists(scriptPath))
                            {
                                _logger.LogWarning($"Script '{scriptPath}' defined in mapping does not exist");
                            }
                            else
                            {
                                _logger.LogInformation($"Verified script exists: {scriptPath}");
                            }
                        }
                    }
                }

                _logger.LogInformation("Configuration validation completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating configuration");
                return false;
            }
        }

        private string TryResolveScriptsDirectory(string scriptsLocation)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Directory.GetCurrentDirectory();
            
            string[] dirsToTry = new[]
            {
                scriptsLocation, // As provided
                Path.IsPathRooted(scriptsLocation) ? scriptsLocation : null, // If absolute
                Path.Combine(baseDir, scriptsLocation), // Relative to app base
                scriptsLocation.StartsWith("./") ? Path.Combine(baseDir, scriptsLocation.Substring(2)) : null, // Without ./
                Path.Combine(currentDir, scriptsLocation), // Relative to current dir
                Path.Combine(baseDir, "scripts", scriptsLocation.Split('/').Last()), // Script type folder only
                Path.Combine("/app/scripts", scriptsLocation.Split('/').Last()) // Docker standard
            }.Where(d => !string.IsNullOrEmpty(d)).Distinct().ToArray();
            
            foreach (var dir in dirsToTry)
            {
                _logger.LogDebug($"Checking if scripts directory exists: {dir}");
                if (Directory.Exists(dir))
                {
                    return dir;
                }
            }
            
            return null; // No directory found
        }
    }
}
