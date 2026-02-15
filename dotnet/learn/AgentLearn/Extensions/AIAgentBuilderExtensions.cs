using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentLearn.Extensions;

/// <summary>
/// Extension methods for <see cref="AIAgentBuilder"/> to add tool invocation logging.
/// </summary>
public static class AIAgentBuilderExtensions
{
    /// <summary>
    /// Adds function invocation middleware that logs debug messages when a tool is
    /// invoked and when it returns a result.
    /// </summary>
    public static AIAgentBuilder UseToolInvocationLogging(this AIAgentBuilder builder, ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger("AgentLearn.ToolInvocation");

        return builder.Use(async (agent, context, next, ct) =>
        {
            string agentName = agent.Name ?? "UnnamedAgent";
            string args = string.Join(", ", context.Arguments.Select(kvp => $"{kvp.Key}: {kvp.Value}"));

            logger.LogDebug("[{AgentName}] Invoking tool {ToolName}({Arguments})",
                agentName, context.Function.Name, args);

            object? result = await next(context, ct);

            logger.LogDebug("[{AgentName}] Tool {ToolName} returned: {Result}",
                agentName, context.Function.Name, result);

            return result;
        });
    }
}
