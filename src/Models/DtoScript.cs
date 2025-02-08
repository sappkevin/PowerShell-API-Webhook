#nullable enable 

namespace Webhookshell.Models
{
    public class DtoScript
    {
        public string ScriptPath { get; set; } = string.Empty; // Ensures non-null default
        public string? Parameters { get; set; } // Optional parameters
    }

    public class DtoResult
    {
        public string ScriptName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Param { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public class Result<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool IsValid => Success && Data != null && Errors.Count == 0;
        public bool IsNotValid => !IsValid;
    }
}
