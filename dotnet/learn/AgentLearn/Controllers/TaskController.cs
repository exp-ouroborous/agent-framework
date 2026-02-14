// Copyright (c) Microsoft. All rights reserved.

using AgentLearn.Models;
using AgentLearn.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentLearn.Controllers;

[ApiController]
[Route("[controller]")]
public class TaskController(IEnumerable<ITaskHandler> handlers) : ControllerBase
{
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
