using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using Webhookshell.Models;
using Webhookshell.Options;

namespace Webhookshell.Services
{
    public class RecurringJobsService
    {
        private readonly ILogger<RecurringJobsService> _logger;
        private readonly IBackgroundJobService _backgroundJobService;
        private readonly ScriptOptions _options;
        
        public RecurringJobsService(
            ILogger<RecurringJobsService> logger,
            IBackgroundJobService backgroundJobService,
            IOptionsMonitor<ScriptOptions> options)
        {
            _logger = logger;
            _backgroundJobService = backgroundJobService;
            _options = options.CurrentValue;
        }
        
        public void ConfigureRecurringJobs()
        {
            _logger.LogInformation("Configuring recurring jobs");
            
            // Retrieve all scripts that have a RecurringSchedule definition
            if (_options?.Handlers == null)
            {
                _logger.LogWarning("No script handlers configured");
                return;
            }
            
            foreach (var handler in _options.Handlers)
            {
                if (handler.ScriptsMapping == null || !handler.ScriptsMapping.Any())
                {
                    continue;
                }
                
                foreach (var scriptMapping in handler.ScriptsMapping)
                {
                    if (scriptMapping.RecurringSchedule != null)
                    {
                        ConfigureJob(handler, scriptMapping);
                    }
                }
            }
        }
        
        private void ConfigureJob(ScriptHandler handler, ScriptMapping scriptMapping)
        {
            try
            {
                if (string.IsNullOrEmpty(scriptMapping.RecurringSchedule))
                {
                    return;
                }
                
                var jobId = $"{handler.ProcessName}_{scriptMapping.Name}";
                
                var script = new DtoScript
                {
                    Script = scriptMapping.Name,
                    Key = scriptMapping.Key ?? handler.Key ?? _options.DefaultKey,
                    Parameters = scriptMapping.DefaultParameters
                };
                
                _logger.LogInformation($"Registering recurring job '{jobId}' with schedule: {scriptMapping.RecurringSchedule}");
                
                RecurringJob.AddOrUpdate(
                    jobId,
                    () => _backgroundJobService.ExecuteScriptAsync(script),
                    scriptMapping.RecurringSchedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error configuring recurring job for script {scriptMapping.Name}");
            }
        }
    }
}