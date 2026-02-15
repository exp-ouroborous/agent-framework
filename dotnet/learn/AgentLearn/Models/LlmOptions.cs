namespace AgentLearn.Models;

/// <summary>
/// Configuration options for the LLM provider.
/// </summary>
public class LlmOptions
{
    /// <summary>
    /// The LLM provider to use. Valid values: GitHub, OpenAI, AzureOpenAI, Anthropic.
    /// </summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>
    /// The model identifier to use.
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Optional endpoint URL. Required for AzureOpenAI, optional for other providers.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Optional API key. If not set, falls back to environment variables:
    /// - GitHub: GH_PAT
    /// - OpenAI: OPENAI_API_KEY
    /// - AzureOpenAI: AZURE_OPENAI_API_KEY
    /// - Anthropic: ANTHROPIC_API_KEY
    /// </summary>
    public string? ApiKey { get; set; }
}
