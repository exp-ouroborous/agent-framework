using AgentLearn.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AgentLearn.TaskKinds.StoryGenerator;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentLearn.IntegrationTests;

[TestClass]
public class StoryGeneratorWorkflowTests
{
    private static readonly ILoggerFactory TestLoggerFactory = NullLoggerFactory.Instance;

    [TestMethod]
    [Timeout(30_000)]
    public async Task StoryWorkflow_HandlesHITL_ProducesStoryOutput()
    {
        // Arrange — mock returns a canned story regardless of prompt
        Mock<IChatClient> mockClient = MockChatClientHelper.Create(
            (systemPrompt, userMessage) =>
                "Once upon a time, Alice found lucky number 7. She never looked back.");

        AIAgent storyteller = StoryAgents.CreateStoryteller(mockClient.Object, TestLoggerFactory);
        Workflow workflow = StoryAgents.BuildSingleWorkflow(storyteller, TestLoggerFactory);

        // Act — run the HITL workflow, responding to the RequestPort with a character name
        await using StreamingRun run = await InProcessExecution.StreamAsync(
            workflow, "What is the character's name?");

        StoryOutput? result = null;
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent requestInfo:
                    await run.SendResponseAsync(
                        requestInfo.Request.CreateResponse("Alice"));
                    break;

                case WorkflowOutputEvent output:
                    StoryOutput? story = output.As<StoryOutput>();
                    if (story is not null)
                    {
                        result = story;
                    }

                    break;
            }
        }

        // Assert
        Assert.IsNotNull(result, "Expected a StoryOutput from the HITL workflow.");
        Assert.IsTrue(
            result.Story.Contains("Alice", StringComparison.OrdinalIgnoreCase),
            $"Expected story to mention Alice, got: '{result.Story}'");
        mockClient.Verify(
            c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce());
    }

    [TestMethod]
    public async Task StoryGeneratorHandler_RejectsNonSingleMode()
    {
        // Arrange
        Mock<IServiceProvider> mockSp = new();
        Mock<ILogger<StoryGeneratorHandler>> mockLogger = new();
        StoryGeneratorHandler handler = new(mockSp.Object, mockLogger.Object);

        TaskRequest request = new()
        {
            Task = "test",
            Mode = AgentMode.Sequential,
            Kind = TaskKind.StoryGenerator,
        };

        // Act & Assert
        await Assert.ThrowsExceptionAsync<NotSupportedException>(
            () => handler.HandleAsync(request));
    }
}
