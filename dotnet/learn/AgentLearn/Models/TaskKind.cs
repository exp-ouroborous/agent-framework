namespace AgentLearn.Models;

/// <summary>
/// Identifies which task handler should process a request.
/// </summary>
public enum TaskKind
{
    /// <summary>Joke-writing workflows (single, sequential, concurrent).</summary>
    JokeWriter,

    /// <summary>Story generation with human-in-the-loop character naming.</summary>
    StoryGenerator
}
