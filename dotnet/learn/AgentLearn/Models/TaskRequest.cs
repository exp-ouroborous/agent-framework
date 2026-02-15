namespace AgentLearn.Models;

/// <summary>
/// Inbound request to the /task endpoint.
/// </summary>
public class TaskRequest
{
    /// <summary>The prompt or topic to process.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>Which workflow mode to use.</summary>
    public AgentMode Mode { get; set; }

    /// <summary>Which task handler should process this request.</summary>
    public TaskKind Kind { get; set; }
}
