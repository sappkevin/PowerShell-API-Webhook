using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Webhookshell.Interfaces;
using Webhookshell.Models;

namespace Webhookshell.Services
{
    public class ScriptRunner : IScriptRunnerService
    {
        // Asynchronously runs the provided script and captures its output and error
        public async Task<Result<DtoResult>> RunAsync(DtoScript scriptToRun, HttpContext httpContext)
        {
            var shell = GetPowerShellPath(); // Determine the correct PowerShell version to use

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell, // Set the shell (pwsh or powershell.exe)
                    Arguments = $"-File {scriptToRun.ScriptPath}", // Specify the script to run
                    RedirectStandardOutput = true, // Capture standard output
                    RedirectStandardError = true,  // Capture standard error
                    UseShellExecute = false,       // Prevents shell execution to allow redirection
                    CreateNoWindow = true          // Run the process without creating a window
                }
            };

            try
            {
                process.Start();

                // Read output and error asynchronously to avoid blocking the thread
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                // Wait for both output and error to complete
                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync(); // Wait for the process to exit asynchronously

                // Return the execution result, including success status, output, and errors
                return new Result<DtoResult>
                {
                    Success = process.ExitCode == 0, // Success if the process exited with code 0
                    Data = new DtoResult { Output = outputTask.Result, Error = errorTask.Result }
                };
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during script execution
                return new Result<DtoResult>
                {
                    Success = false,
                    Data = new DtoResult { Output = string.Empty, Error = ex.Message }
                };
            }
        }

        // Determines the correct PowerShell executable based on the operating system
        private string GetPowerShellPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (IsPowerShellCoreInstalled()) // Prefer PowerShell Core if installed
                    return "pwsh";

                return "powershell.exe"; // Fallback to Windows PowerShell
            }
            else
            {
                return "pwsh"; // On Linux/macOS, always use PowerShell Core
            }
        }

        // Checks if PowerShell Core is installed on the system
        private bool IsPowerShellCoreInstalled()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pwsh",       // Attempt to run pwsh to check for installation
                        Arguments = "-Version", // Check version as a lightweight command
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0; // If the exit code is 0, PowerShell Core is installed
            }
            catch
            {
                return false; // If an error occurs, PowerShell Core is not installed
            }
        }
    }
}
