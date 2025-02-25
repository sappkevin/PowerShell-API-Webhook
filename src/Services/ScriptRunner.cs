using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Webhookshell.Interfaces;
using Webhookshell.Models;
using System;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;

namespace Webhookshell.Services
{
    public class ScriptRunner : IScriptRunnerService
    {
        private readonly ILogger<ScriptRunner> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _scriptLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        
        // Max concurrent scripts per script name to prevent resource exhaustion
        private const int MaxConcurrentExecutionsPerScript = 5;
        
        public ScriptRunner(ILogger<ScriptRunner> logger)
        {
            _logger = logger;
        }

        // Interface implementation for synchronous compatibility
        public Result<DtoResult> Run(DtoScript scriptToRun, HttpContext httpContext)
        {
            return RunAsync(scriptToRun, httpContext).GetAwaiter().GetResult();
        }

        // Asynchronous script execution method
        public async Task<Result<DtoResult>> RunAsync(DtoScript scriptToRun, HttpContext httpContext)
        {
            // Get or create a semaphore for this specific script to limit concurrent executions
            var scriptSemaphore = _scriptLocks.GetOrAdd(
                scriptToRun.Script, 
                _ => new SemaphoreSlim(MaxConcurrentExecutionsPerScript, MaxConcurrentExecutionsPerScript)
            );
            
            // Try to acquire the semaphore with a timeout
            bool acquired = false;
            try
            {
                acquired = await scriptSemaphore.WaitAsync(TimeSpan.FromSeconds(30));
                if (!acquired)
                {
                    _logger.LogWarning($"Request timeout while waiting to execute script {scriptToRun.Script}");
                    return new Result<DtoResult>
                    {
                        Success = false,
                        Data = new DtoResult { Error = "Request timed out while waiting for resources. Too many concurrent executions." }
                    };
                }
                
                return await ExecuteScriptAsync(scriptToRun);
            }
            finally
            {
                if (acquired)
                {
                    scriptSemaphore.Release();
                }
            }
        }
        
        private async Task<Result<DtoResult>> ExecuteScriptAsync(DtoScript scriptToRun)
        {
            var shell = GetPowerShellPath();
            var result = new Result<DtoResult> { Data = new DtoResult() };
            
            // Log detailed information about script execution
            _logger.LogInformation($"Executing script: {scriptToRun.Script} at path: {scriptToRun.ScriptPath}");
            
            // Verify script exists
            if (!File.Exists(scriptToRun.ScriptPath))
            {
                _logger.LogError($"Script not found at path: {scriptToRun.ScriptPath}");
                result.Success = false;
                result.Data.Error = $"Script file not found: {scriptToRun.Script}";
                return result;
            }
            
            // Make the script path absolute if it's relative
            string scriptFullPath = Path.GetFullPath(scriptToRun.ScriptPath);
            _logger.LogInformation($"Full script path: {scriptFullPath}");
            
            // Prepare command arguments properly
            string arguments = $"-File \"{scriptFullPath}\"";
            if (!string.IsNullOrEmpty(scriptToRun.Parameters))
            {
                arguments += $" {scriptToRun.Parameters}";
            }
            
            _logger.LogInformation($"PowerShell command: {shell} {arguments}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptFullPath) // Set working directory to script location
                }
            };

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // 5 minute timeout
                
                _logger.LogInformation("Starting PowerShell process...");
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                // Create a task that completes when the process exits
                var processTask = Task.Run(async () => {
                    try
                    {
                        await process.WaitForExitAsync(cts.Token);
                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(true); // Kill entire process tree
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error killing process after timeout");
                        }
                        return false;
                    }
                });

                // Wait for either the process to exit or the timeout to occur
                var completedTask = await Task.WhenAny(processTask, Task.Delay(TimeSpan.FromMinutes(5)));
                
                if (completedTask != processTask || !await processTask)
                {
                    result.Success = false;
                    result.Data.Error = "Script execution timed out after 5 minutes";
                    return result;
                }

                // Now that we know the process has exited, get output and error
                var output = await outputTask;
                var error = await errorTask;
                
                _logger.LogInformation($"Process exit code: {process.ExitCode}");
                _logger.LogInformation($"Process output: {output}");
                
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogError($"Process error: {error}");
                }
                
                result.Data.Output = output;
                result.Data.Error = error;
                result.Success = process.ExitCode == 0;
                
                // Populate additional information
                result.Data.ScriptName = scriptToRun.Script;
                result.Data.Param = scriptToRun.Parameters ?? string.Empty;
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing script {scriptToRun.Script}");
                return new Result<DtoResult>
                {
                    Success = false,
                    Data = new DtoResult { 
                        ScriptName = scriptToRun.Script,
                        Param = scriptToRun.Parameters ?? string.Empty,
                        Output = string.Empty, 
                        Error = $"Error executing script: {ex.Message}" 
                    }
                };
            }
        }

        private string GetPowerShellPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (IsPowerShellCoreInstalled())
                {
                    _logger.LogInformation("Using PowerShell Core (pwsh) on Windows");
                    return "pwsh";
                }
                _logger.LogInformation("Using Windows PowerShell (powershell.exe)");
                return "powershell.exe";
            }
            _logger.LogInformation("Using PowerShell Core (pwsh) on non-Windows platform");
            return "pwsh";
        }

        private bool IsPowerShellCoreInstalled()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pwsh",
                        Arguments = "-Version",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking for PowerShell Core installation");
                return false;
            }
        }
    }
}