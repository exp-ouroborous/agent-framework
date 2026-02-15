namespace AgentLearn.Models;

/// <summary>
/// The workflow execution mode for a task.
/// </summary>
public enum AgentMode
{
    /// <summary>A single agent handles the task.</summary>
    Single,

    /// <summary>Agents execute sequentially in a pipeline.</summary>
    Sequential,

    /// <summary>Multiple pipelines run concurrently then a selector picks the best result.</summary>
    Concurrent
}
