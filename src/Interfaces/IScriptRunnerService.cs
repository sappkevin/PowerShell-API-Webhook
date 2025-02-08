using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Webhookshell.Models;

namespace Webhookshell.Interfaces
{
    public interface IScriptRunnerService
    {
        Result<DtoResult> Run(DtoScript scriptToRun, HttpContext httpContext);
        Task<Result<DtoResult>> RunAsync(DtoScript scriptToRun, HttpContext httpContext);
    }
}
