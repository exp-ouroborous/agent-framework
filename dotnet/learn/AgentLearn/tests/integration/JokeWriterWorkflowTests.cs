using AgentLearn.TaskKinds.JokeWriter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentLearn.IntegrationTests;

[TestClass]
public class JokeWriterWorkflowTests
{
    private static readonly ILoggerFactory TestLoggerFactory = NullLoggerFactory.Instance;

    /// <summary>
    /// Switches on keywords in the agent's system prompt to return role-appropriate text.
    /// </summary>
    private static string GenerateResponse(string systemPrompt, string lastUserMessage)
    {
        return systemPrompt switch
        {
            // Check "editor" before "critic" — EditorInstructions contains "criticism"
            _ when systemPrompt.Contains("editor", StringComparison.OrdinalIgnoreCase)
                => "Edited joke: Why did the test topic cross the road on day 42?",
            _ when systemPrompt.Contains("critic", StringComparison.OrdinalIgnoreCase)
                => "Criticism: The joke could be funnier with more wordplay.",
            _ when systemPrompt.Contains("select", StringComparison.OrdinalIgnoreCase)
                => "Selected: The best test topic joke of day 42!",
            _ when systemPrompt.Contains("liberal", StringComparison.OrdinalIgnoreCase)
                => "Liberal take: A progressive test topic joke for day 42.",
            _ when systemPrompt.Contains("conservative", StringComparison.OrdinalIgnoreCase)
                => "Conservative take: A traditional test topic joke for day 42.",
            _ when systemPrompt.Contains("neutral", StringComparison.OrdinalIgnoreCase)
                => "Neutral take: A balanced test topic joke for day 42.",
            _ => "Test joke: Why did test topic laugh? Day 42!",
        };
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task SingleWorkflow_ProducesJokeOutput()
    {
        // Arrange
        Mock<IChatClient> mockClient = MockChatClientHelper.Create(GenerateResponse);
        AIAgent writer = JokeAgents.CreateWriter(mockClient.Object, TestLoggerFactory);
        Workflow workflow = JokeAgents.BuildSingleWorkflow(writer, TestLoggerFactory);

        // Act
        JokeOutput? result = await RunJokeWorkflowAsync(workflow, "test topic");

        // Assert
        Assert.IsNotNull(result, "Expected a JokeOutput from the single workflow.");
        Assert.IsTrue(
            result.Joke.Contains("Test joke", StringComparison.OrdinalIgnoreCase),
            $"Expected joke to contain 'Test joke', got: '{result.Joke}'");
        mockClient.Verify(
            c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce());
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task SequentialWorkflow_ProducesEditedOutput()
    {
        // Arrange
        Mock<IChatClient> mockClient = MockChatClientHelper.Create(GenerateResponse);
        IChatClient client = mockClient.Object;
        AIAgent writer = JokeAgents.CreateWriter(client, TestLoggerFactory);
        AIAgent critic = JokeAgents.CreateCritic(client);
        AIAgent editor = JokeAgents.CreateEditor(client);
        Workflow workflow = JokeAgents.BuildSequentialWorkflow(writer, critic, editor, TestLoggerFactory);

        // Act
        JokeOutput? result = await RunJokeWorkflowAsync(workflow, "test topic");

        // Assert
        Assert.IsNotNull(result, "Expected a JokeOutput from the sequential workflow.");
        Assert.IsTrue(
            result.Joke.Contains("Edited joke", StringComparison.OrdinalIgnoreCase),
            $"Expected joke text from editor, got: '{result.Joke}'");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConcurrentWorkflow_ProducesSelectorOutput()
    {
        // Arrange
        Mock<IChatClient> mockClient = MockChatClientHelper.Create(GenerateResponse);
        IChatClient client = mockClient.Object;
        Workflow[] pipelines =
        [
            JokeAgents.CreateNeutralPipeline(client, TestLoggerFactory),
            JokeAgents.CreateLiberalPipeline(client, TestLoggerFactory),
            JokeAgents.CreateConservativePipeline(client, TestLoggerFactory),
        ];
        AIAgent selector = JokeAgents.CreateSelector(client);
        Workflow workflow = JokeAgents.BuildConcurrentWorkflow(pipelines, selector, TestLoggerFactory);

        // Act
        JokeOutput? result = await RunJokeWorkflowAsync(workflow, "test topic");

        // Assert
        Assert.IsNotNull(result, "Expected a JokeOutput from the concurrent workflow.");
        Assert.IsTrue(
            result.Joke.Contains("Selected", StringComparison.OrdinalIgnoreCase),
            $"Expected joke text from selector, got: '{result.Joke}'");
    }

    private static async Task<JokeOutput?> RunJokeWorkflowAsync(Workflow workflow, string topic)
    {
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, new JokeRequest(topic));

        JokeOutput? result = null;
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output)
            {
                JokeOutput? joke = output.As<JokeOutput>();
                if (joke is not null)
                {
                    result = joke;
                }
            }
        }

        return result;
    }
}
