using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Webhookshell.Models;
using Webhookshell.Interfaces;

namespace Webhookshell.Services
{
    public interface IBackgroundJobService
    {
        string EnqueueScript(DtoScript script);
        Task<Result<DtoResult>> ExecuteScriptAsync(DtoScript script);
    }
    
    public class BackgroundJobService : IBackgroundJobService
    {
        private readonly ILogger<BackgroundJobService> _logger;
        private readonly IScriptRunnerService _scriptRunner;
        
        public BackgroundJobService(
            ILogger<BackgroundJobService> logger,
            IScriptRunnerService scriptRunner)
        {
            _logger = logger;
            _scriptRunner = scriptRunner;
        }
        
        public string EnqueueScript(DtoScript script)
        {
            _logger.LogInformation($"Enqueueing script {script.Script} for background execution");
            return BackgroundJob.Enqueue(() => ExecuteScriptAsync(script));
        }
        
        public async Task<Result<DtoResult>> ExecuteScriptAsync(DtoScript script)
        {
            try
            {
                _logger.LogInformation($"Executing background script: {script.Script}");
                // We don't have HttpContext here, so we pass null
                var result = await _scriptRunner.RunAsync(script, null);
                
                // Log job completion status
                if (result.IsValid)
                {
                    _logger.LogInformation($"Background script {script.Script} completed successfully");
                }
                else
                {
                    _logger.LogWarning($"Background script {script.Script} completed with errors: {string.Join(", ", result.Errors)}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing background script {script.Script}");
                return new Result<DtoResult>
                {
                    Success = false,
                    Data = new DtoResult
                    {
                        ScriptName = script.Script,
                        Error = $"Background job error: {ex.Message}"
                    },
                    Errors = { ex.Message }
                };
            }
        }
    }
}