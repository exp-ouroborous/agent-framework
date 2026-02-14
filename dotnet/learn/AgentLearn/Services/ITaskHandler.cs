// Copyright (c) Microsoft. All rights reserved.

using AgentLearn.Models;

namespace AgentLearn.Services;

public interface ITaskHandler
{
    TaskKind Kind { get; }
    Task<string> HandleAsync(TaskRequest request);
}
