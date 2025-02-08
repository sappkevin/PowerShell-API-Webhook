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
        // Interface implementation for synchronous compatibility
        public Result<DtoResult> Run(DtoScript scriptToRun, HttpContext httpContext)
        {
            return RunAsync(scriptToRun, httpContext).GetAwaiter().GetResult();
        }

        // Asynchronous script execution method
        public async Task<Result<DtoResult>> RunAsync(DtoScript scriptToRun, HttpContext httpContext)
        {
            var shell = GetPowerShellPath();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"-File {scriptToRun.ScriptPath}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync();

                return new Result<DtoResult>
                {
                    Success = process.ExitCode == 0,
                    Data = new DtoResult { Output = outputTask.Result, Error = errorTask.Result }
                };
            }
            catch (Exception ex)
            {
                return new Result<DtoResult>
                {
                    Success = false,
                    Data = new DtoResult { Output = string.Empty, Error = ex.Message }
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
