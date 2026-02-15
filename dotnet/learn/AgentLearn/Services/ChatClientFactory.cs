using System.ClientModel;
using AgentLearn.Models;
using Anthropic;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgentLearn.Services;

/// <summary>
/// Factory for creating IChatClient instances based on configuration.
/// </summary>
public static class ChatClientFactory
{
    /// <summary>
    /// Creates an IChatClient based on the provided LlmOptions.
    /// </summary>
    public static IChatClient Create(LlmOptions options, IHostEnvironment environment)
    {
        IChatClient innerClient = options.Provider.ToUpperInvariant() switch
        {
            "GITHUB" => CreateGitHubClient(options),
            "OPENAI" => CreateOpenAIClient(options),
            "AZUREOPENAI" => CreateAzureOpenAIClient(options),
            "ANTHROPIC" => CreateAnthropicClient(options),
            "MOCK" => new MockChatClient(),
            _ => throw new InvalidOperationException($"Unknown LLM provider: {options.Provider}. Valid values: GitHub, OpenAI, AzureOpenAI, Anthropic, Mock.")
        };

        return new ChatClientBuilder(innerClient)
            .UseOpenTelemetry(sourceName: "AgentLearn.AI", configure: otel =>
            {
                // Include message content in traces only in development mode
                if (environment.IsDevelopment())
                {
                    otel.EnableSensitiveData = true;
                }
            })
            .Build();
    }

    private static IChatClient CreateGitHubClient(LlmOptions options)
    {
        string apiKey = options.ApiKey
            ?? Environment.GetEnvironmentVariable("GH_PAT")
            ?? throw new InvalidOperationException("GitHub PAT not configured. Set Llm:ApiKey in configuration or GH_PAT environment variable.");

        string endpoint = options.Endpoint ?? "https://models.github.ai/inference";

        return new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetChatClient(options.Model)
            .AsIChatClient();
    }

    private static IChatClient CreateOpenAIClient(LlmOptions options)
    {
        string apiKey = options.ApiKey
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OpenAI API key not configured. Set Llm:ApiKey in configuration or OPENAI_API_KEY environment variable.");

        OpenAIClientOptions? clientOptions = null;
        if (!string.IsNullOrEmpty(options.Endpoint))
        {
            clientOptions = new OpenAIClientOptions { Endpoint = new Uri(options.Endpoint) };
        }

        return new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions)
            .GetChatClient(options.Model)
            .AsIChatClient();
    }

    private static IChatClient CreateAzureOpenAIClient(LlmOptions options)
    {
        string endpoint = options.Endpoint
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("Azure OpenAI endpoint not configured. Set Llm:Endpoint in configuration or AZURE_OPENAI_ENDPOINT environment variable.");

        string apiKey = options.ApiKey
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? throw new InvalidOperationException("Azure OpenAI API key not configured. Set Llm:ApiKey in configuration or AZURE_OPENAI_API_KEY environment variable.");

        return new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey))
            .GetChatClient(options.Model)
            .AsIChatClient();
    }

    private static IChatClient CreateAnthropicClient(LlmOptions options)
    {
        string apiKey = options.ApiKey
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("Anthropic API key not configured. Set Llm:ApiKey in configuration or ANTHROPIC_API_KEY environment variable.");

        AnthropicClient client = new() { ApiKey = apiKey };
        return client.AsIChatClient(options.Model);
    }
}

/// <summary>
/// Mock chat client for testing workflows without hitting a real LLM.
/// Returns canned responses based on the agent role detected from system prompts.
/// </summary>
public sealed class MockChatClient : IChatClient
{
    private static readonly int s_dayOfYear = DateTime.Now.DayOfYear;
    private readonly Dictionary<string, bool> _toolCallsMade = new();

    /// <inheritdoc />
    public ChatClientMetadata Metadata { get; } = new("MockChatClient", new Uri("http://localhost"), "mock-model");

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<ChatMessage> messageList = [.. messages];
        string? systemPrompt = messageList.FirstOrDefault(m => m.Role == ChatRole.System)?.Text?.ToLowerInvariant() ?? "";

        // Add small delay to simulate real LLM latency
        await Task.Delay(100, cancellationToken);

        // Generate response - the mock returns appropriate canned responses
        string text = GenerateResponse(systemPrompt, messageList);
        ChatMessage message = new(ChatRole.Assistant, text);
        return new ChatResponse(message);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Simulate streaming by getting the full response and yielding it as a single update
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
        ChatMessage msg = response.Messages.First();

        yield return new ChatResponseUpdate(msg.Role, msg.Text ?? "");
    }

    /// <inheritdoc />
    public void Dispose() { }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? key = null) => null;

    private static string GenerateResponse(string systemPrompt, List<ChatMessage> messages)
    {
        // Detect agent type from system prompt
        if (systemPrompt.Contains("critic"))
        {
            return $"[MOCK CRITIC] The joke has potential but could be improved. The setup is decent, but the punchline needs more punch. Consider making the connection to day {s_dayOfYear} more surprising.";
        }

        if (systemPrompt.Contains("editor"))
        {
            return $"[MOCK EDITED] Why did the electric car break up with the gas station on day {s_dayOfYear}? Because it found a more electrifying relationship at the charging station!";
        }

        if (systemPrompt.Contains("select"))
        {
            return "[MOCK SELECTED] Why did the electric car break up with the gas station? Because it found a more electrifying relationship!";
        }

        if (systemPrompt.Contains("liberal"))
        {
            return $"[MOCK LIBERAL] On day {s_dayOfYear}, an electric car said to a gas car: 'I'm saving the planet one charge at a time!'";
        }

        if (systemPrompt.Contains("conservative"))
        {
            return $"[MOCK CONSERVATIVE] Day {s_dayOfYear} fun fact: Electric cars are so quiet, they had to add fake engine noise!";
        }

        if (systemPrompt.Contains("neutral") || systemPrompt.Contains("balanced"))
        {
            return $"[MOCK NEUTRAL] It's day {s_dayOfYear} and my electric car's battery died. I called for a tow truck. It was also electric. We're still waiting.";
        }

        // Default writer response
        return $"[MOCK WRITER] Why do electric cars make terrible comedians on day {s_dayOfYear}? Because their jokes have no gas!";
    }
}
