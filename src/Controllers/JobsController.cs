using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Webhookshell.Interfaces;
using Webhookshell.Models;
using Webhookshell.Services;
using Webhookshell.Validators;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Hangfire;

namespace Webhookshell.Controllers
{
    [ApiController]
    [Route("[controller]/v1/")]
    [Produces("application/json")]
    public class JobsController : ControllerBase
    {
        private readonly IBackgroundJobService _backgroundJobService;
        private readonly IScriptValidationService _validationService;
        private readonly DtoScriptValidator _scriptValidator;
        private readonly ILogger<JobsController> _logger;
        
        public JobsController(
            IBackgroundJobService backgroundJobService,
            IScriptValidationService validationService,
            DtoScriptValidator scriptValidator,
            ILogger<JobsController> logger)
        {
            _backgroundJobService = backgroundJobService;
            _validationService = validationService;
            _scriptValidator = scriptValidator;
            _logger = logger;
        }
        
        /// <summary>
        /// Enqueues a script to be executed as a background job
        /// </summary>
        /// <param name="scriptToEnqueue">Script execution details</param>
        /// <returns>Job ID for the enqueued task</returns>
        [HttpPost("enqueue")]
        public IActionResult EnqueueJob([FromBody] DtoScript scriptToEnqueue)
        {
            // Validate input parameters
            var validationErrors = _scriptValidator.ValidateInput(scriptToEnqueue);
            if (validationErrors.Any())
            {
                return BadRequest(validationErrors);
            }
            
            // Validate the script via the validation service
            var validationResult = _validationService.Validate(scriptToEnqueue, HttpContext);
            if (validationResult.IsNotValid)
            {
                return BadRequest(validationResult.Errors);
            }
            
            // Enqueue the script for background execution
            string jobId = _backgroundJobService.EnqueueScript(scriptToEnqueue);
            
            return Ok(new { JobId = jobId, Message = "Script enqueued for background execution" });
        }
        
        /// <summary>
        /// Retrieves the status of a specific job
        /// </summary>
        /// <param name="id">Job ID</param>
        /// <returns>Job status information</returns>
        [HttpGet("status/{id}")]
        public IActionResult GetJobStatus(string id)
        {
            var job = JobStorage.Current.GetConnection().GetJobData(id);
            if (job == null)
            {
                return NotFound(new { Message = $"Job with ID {id} not found" });
            }
            
            return Ok(new
            {
                JobId = id,
                State = job.State,
                CreatedAt = job.CreatedAt,
                Status = GetFriendlyStatus(job.State)
            });
        }
        
        private string GetFriendlyStatus(string state)
        {
            return state switch
            {
                "Succeeded" => "Completed successfully",
                "Failed" => "Failed",
                "Processing" => "Running",
                "Scheduled" => "Scheduled",
                "Enqueued" => "Waiting to run",
                _ => state
            };
        }
    }
}