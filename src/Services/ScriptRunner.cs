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
            
            // Prepare command arguments properly
            string arguments = $"-File \"{scriptToRun.ScriptPath}\"";
            if (!string.IsNullOrEmpty(scriptToRun.Parameters))
            {
                arguments += $" {scriptToRun.Parameters}";
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // 5 minute timeout
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
                result.Data.Output = await outputTask;
                result.Data.Error = await errorTask;
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
                    return "pwsh";
                return "powershell.exe";
            }
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
            catch
            {
                return false;
            }
        }
    }
}