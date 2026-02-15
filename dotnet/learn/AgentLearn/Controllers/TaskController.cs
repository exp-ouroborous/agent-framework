using AgentLearn.Models;
using AgentLearn.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentLearn.Controllers;

/// <summary>
/// REST endpoint for submitting tasks to agent workflows.
/// </summary>
[ApiController]
[Route("[controller]")]
public class TaskController(IEnumerable<ITaskHandler> handlers) : ControllerBase
{
    /// <summary>
    /// Dispatches a task request to the matching <see cref="ITaskHandler"/> and returns the result.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ProcessTask([FromBody] TaskRequest request)
    {
        ITaskHandler? handler = handlers.FirstOrDefault(h => h.Kind == request.Kind);
        if (handler is null)
        {
            return this.BadRequest($"No handler found for kind: {request.Kind}");
        }

        string result = await handler.HandleAsync(request);

        return this.Ok(new TaskResponse
        {
            Task = request.Task,
            Mode = request.Mode,
            Kind = request.Kind,
            Result = result
        });
    }
}
