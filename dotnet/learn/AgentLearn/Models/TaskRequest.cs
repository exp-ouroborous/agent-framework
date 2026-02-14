// Copyright (c) Microsoft. All rights reserved.

namespace AgentLearn.Models;

public class TaskRequest
{
    public string Task { get; set; } = string.Empty;
    public AgentMode Mode { get; set; }
    public TaskKind Kind { get; set; }
}
