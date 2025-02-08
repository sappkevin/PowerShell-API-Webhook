#nullable enable

namespace Webhookshell.Models
{
    public class ScriptMapping
    {
        public string Name { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public Trigger? Trigger { get; set; }
    }
}
