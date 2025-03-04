using Hangfire.Dashboard;

namespace Webhookshell.Services
{
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            // In development, allow all
            // In production, implement proper authorization
            return true;
        }
    }
}