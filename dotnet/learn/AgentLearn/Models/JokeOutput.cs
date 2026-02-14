// Copyright (c) Microsoft. All rights reserved.

namespace AgentLearn.Models;

public record JokeOutput(string Joke)
{
    public override string ToString() => Joke;
}
