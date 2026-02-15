using Microsoft.Extensions.AI;
using Moq;

namespace AgentLearn.IntegrationTests;

/// <summary>
/// Creates <see cref="Mock{IChatClient}"/> instances whose responses are driven by a delegate.
/// </summary>
internal static class MockChatClientHelper
{
    /// <summary>
    /// Creates a <see cref="Mock{IChatClient}"/> that routes every call through
    /// <paramref name="responseGenerator"/>. The delegate receives the system prompt
    /// (from the first <see cref="ChatRole.System"/> message) and the last user message text,
    /// and returns the assistant response text.
    /// </summary>
    internal static Mock<IChatClient> Create(Func<string, string, string>? responseGenerator = null)
    {
        responseGenerator ??= (_, _) => "[Test response]";

        Mock<IChatClient> mock = new();

        mock.Setup(c => c.GetService(It.IsAny<Type>(), It.IsAny<object?>()))
            .Returns((Type serviceType, object? _) =>
                serviceType.IsInstanceOfType(mock.Object) ? mock.Object : null);

        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken _) =>
            {
                (string systemPrompt, string lastUserMsg) = ExtractPrompts(messages, options);
                string responseText = responseGenerator(systemPrompt, lastUserMsg);

                ChatResponse response = new([new ChatMessage(ChatRole.Assistant, responseText)])
                {
                    ModelId = "test-model",
                    FinishReason = ChatFinishReason.Stop,
                };
                return Task.FromResult(response);
            });

        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken _) =>
            {
                (string systemPrompt, string lastUserMsg) = ExtractPrompts(messages, options);
                string responseText = responseGenerator(systemPrompt, lastUserMsg);

                return YieldSingle(new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(responseText)]
                });
            });

        return mock;
    }

    private static (string SystemPrompt, string LastUserMessage) ExtractPrompts(
        IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        List<ChatMessage> msgList = messages.ToList();

        // ChatClientAgent passes instructions via ChatOptions.Instructions
        string systemPrompt = options?.Instructions
            ?? msgList.FirstOrDefault(m => m.Role == ChatRole.System)?.Text
            ?? "";

        string lastUserMsg = msgList
            .LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        return (systemPrompt, lastUserMsg);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> YieldSingle(ChatResponseUpdate update)
    {
        yield return update;
        await Task.CompletedTask;
    }
}
