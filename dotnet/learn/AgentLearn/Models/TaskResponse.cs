namespace AgentLearn.Models;

/// <summary>
/// Response returned by the /task endpoint.
/// </summary>
public class TaskResponse
{
    /// <summary>The original prompt or topic.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>The workflow mode that was used.</summary>
    public AgentMode Mode { get; set; }

    /// <summary>The task kind that handled the request.</summary>
    public TaskKind Kind { get; set; }

    /// <summary>The generated result text.</summary>
    public string Result { get; set; } = string.Empty;
}
