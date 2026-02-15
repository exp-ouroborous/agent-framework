using AgentLearn.Models;

namespace AgentLearn.Services;

/// <summary>
/// Handles a task request for a specific <see cref="TaskKind"/>.
/// </summary>
public interface ITaskHandler
{
    /// <summary>The kind of task this handler supports.</summary>
    TaskKind Kind { get; }

    /// <summary>Executes the workflow and returns the result text.</summary>
    Task<string> HandleAsync(TaskRequest request);
}
