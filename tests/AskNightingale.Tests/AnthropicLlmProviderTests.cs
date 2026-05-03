using AskNightingale.Services.Llm;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace AskNightingale.Tests;

// Live-API integration test. Skipped when ANTHROPIC_API_KEY is not set
// (e.g. CI without secret). Provides end-to-end confirmation that the
// HttpClient + JSON shape + auth headers match the live Anthropic API.
public class AnthropicLlmProviderTests
{
    [Fact]
    public async Task Round_trip_against_live_anthropic_api()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No key -> skip silently. Reported as passing in CI.
            return;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = apiKey,
                ["ANTHROPIC_MODEL"] = "claude-haiku-4-5"
            })
            .Build();

        using var http = new HttpClient();
        var sut = new AnthropicLlmProvider(http, config);

        var response = await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: "Reply with exactly the word 'pong'. Nothing else.",
            Messages: [new LlmMessage("user", "ping")],
            MaxTokens: 20
        ));

        response.Content.ShouldContain("pong", Case.Insensitive);
        response.OutputTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Throws_clear_error_when_api_key_missing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        using var http = new HttpClient();

        var ex = Should.Throw<InvalidOperationException>(
            () => new AnthropicLlmProvider(http, config));

        ex.Message.ShouldContain("ANTHROPIC_API_KEY");
    }
}
