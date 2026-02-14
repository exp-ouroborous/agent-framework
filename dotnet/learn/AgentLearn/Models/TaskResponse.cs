// Copyright (c) Microsoft. All rights reserved.

namespace AgentLearn.Models;

public class TaskResponse
{
    public string Task { get; set; } = string.Empty;
    public AgentMode Mode { get; set; }
    public TaskKind Kind { get; set; }
    public string Result { get; set; } = string.Empty;
}
