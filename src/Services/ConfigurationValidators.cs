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

                    // Verify scripts directory exists
                    if (!Directory.Exists(handler.ScriptsLocation))
                    {
                        _logger.LogWarning($"Scripts directory '{handler.ScriptsLocation}' for handler '{handler.ProcessName}' does not exist. Creating it...");
                        try
                        {
                            Directory.CreateDirectory(handler.ScriptsLocation);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to create scripts directory '{handler.ScriptsLocation}'");
                            return false;
                        }
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

                            // Check if script exists
                            string scriptPath = Path.Combine(handler.ScriptsLocation, mapping.Name);
                            if (!File.Exists(scriptPath))
                            {
                                _logger.LogWarning($"Script '{scriptPath}' defined in mapping does not exist");
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
    }
}