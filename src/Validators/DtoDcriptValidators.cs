using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Webhookshell.Models;

namespace Webhookshell.Validators
{
    public class DtoScriptValidator
    {
        private readonly ILogger<DtoScriptValidator> _logger;
        
        public DtoScriptValidator(ILogger<DtoScriptValidator> logger)
        {
            _logger = logger;
        }
        
        public List<string> ValidateInput(DtoScript script)
        {
            var errors = new List<string>();
            
            if (script == null)
            {
                errors.Add("Script information cannot be null");
                return errors;
            }
            
            // Validate script name
            if (string.IsNullOrWhiteSpace(script.Script))
            {
                errors.Add("Script name is required");
            }
            else
            {
                // Check for unsafe characters that could lead to path traversal
                if (script.Script.Contains("..") || 
                    script.Script.Contains('\\') || 
                    script.Script.Contains('/'))
                {
                    errors.Add("Script name contains invalid characters. Only alphanumeric characters, hyphens, and file extensions are allowed.");
                }
                
                // Check for valid file extensions
                string[] allowedExtensions = { ".ps1", ".py" };
                bool hasValidExtension = false;
                
                foreach (var ext in allowedExtensions)
                {
                    if (script.Script.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        hasValidExtension = true;
                        break;
                    }
                }
                
                if (!hasValidExtension)
                {
                    errors.Add($"Script must have one of the following extensions: {string.Join(", ", allowedExtensions)}");
                }
            }
            
            // Validate key
            if (string.IsNullOrWhiteSpace(script.Key))
            {
                errors.Add("Security key is required");
            }
            
            // Validate parameters (if present)
            if (!string.IsNullOrWhiteSpace(script.Parameters))
            {
                // Check for potentially dangerous parameters
                if (ContainsDangerousParameters(script.Parameters))
                {
                    errors.Add("Parameters contain potentially unsafe commands or characters");
                }
            }
            
            return errors;
        }
        
        private bool ContainsDangerousParameters(string parameters)
        {
            // List of potentially dangerous PowerShell commands or patterns
            string[] dangerousPatterns = {
                @"Invoke-Expression", @"iex\s+", @"Invoke-Command", @"Start-Process",
                @"Net\s+User", @"New-Service", @"Stop-Service", @"Remove-Item\s+.+\s+-Recurse",
                @"Format-Volume", @"Clear-Disk", @"Initialize-Disk"
            };
            
            foreach (var pattern in dangerousPatterns)
            {
                if (Regex.IsMatch(parameters, pattern, RegexOptions.IgnoreCase))
                {
                    _logger.LogWarning($"Potentially dangerous parameter detected: {parameters}");
                    return true;
                }
            }
            
            return false;
        }
    }
}