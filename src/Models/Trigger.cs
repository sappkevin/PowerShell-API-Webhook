using System.Collections.Generic;
#nullable enable

namespace Webhookshell.Models
{
    public class Trigger
    {
        public HttpTriggerMethod? HttpMethod { get; set; }
        public IList<string> IpAddresses { get; set; } = new List<string>();
        public IList<TimeFrame?> TimeFrames { get; set; } = new List<TimeFrame?>();
    }
}
