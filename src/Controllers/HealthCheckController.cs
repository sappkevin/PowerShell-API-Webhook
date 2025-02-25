using Microsoft.AspNetCore.Mvc;
using System;

namespace Webhookshell.Controllers
{
    /// <summary>
    /// Controller for health check endpoints
    /// </summary>
    [ApiController]
    [Route("health")]
    public class HealthCheckController : ControllerBase
    {
        /// <summary>
        /// Returns a basic health status of the API
        /// </summary>
        /// <returns>Health status information</returns>
        [HttpGet]
        public IActionResult GetHealth()
        {
            var status = new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Version = GetType().Assembly.GetName().Version.ToString()
            };

            return Ok(status);
        }

        /// <summary>
        /// Returns detailed health information for the API
        /// </summary>
        /// <returns>Detailed health information</returns>
        [HttpGet("detailed")]
        public IActionResult GetDetailedHealth()
        {
            var status = new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Version = GetType().Assembly.GetName().Version.ToString(),
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                OperatingSystem = Environment.OSVersion.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                DotNetVersion = Environment.Version.ToString(),
                ProcessUptime = (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString()
            };

            return Ok(status);
        }