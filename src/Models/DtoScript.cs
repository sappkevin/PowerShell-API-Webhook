namespace Webhookshell.Models
{
    public class DtoScript
    {
        public string ScriptPath { get; set; } = string.Empty; // Ensures non-null default
        public string? Parameters { get; set; } // Optional parameters
    }

    public class DtoResult
    {
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public class Result<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string? Errors { get; set; } // For any additional error messages

        public bool IsValid => Success && Data != null;
    }
}
