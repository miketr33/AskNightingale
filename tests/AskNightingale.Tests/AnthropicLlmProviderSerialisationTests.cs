using System.Net;
using System.Text;
using AskNightingale.Services.Llm;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace AskNightingale.Tests;

// Wire-format regression tests. Hits the actual JSON serialisation path
// by intercepting the HTTP request, capturing the body, and asserting
// shape. The unit tests on LlmChatService fake the LLM at the interface
// boundary and never exercise this layer — the eval caught a real null-
// system serialisation bug we'd missed; these tests guard against the
// reverse mistake (omitting JsonPropertyName, which silently drops the
// system prompt under a wrong-cased field name Anthropic ignores).
public class AnthropicLlmProviderSerialisationTests
{
    [Fact]
    public async Task Omits_system_field_entirely_when_system_prompt_is_null()
    {
        var (sut, captured) = MakeSut();

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "hi")]
        ));

        var body = captured.ToString();
        body.ShouldNotContain("\"system\"", Case.Sensitive);
        body.ShouldNotContain("\"System\"", Case.Sensitive);
    }

    [Fact]
    public async Task Sends_lowercase_system_field_when_system_prompt_is_set()
    {
        var (sut, captured) = MakeSut();

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: "you are an assistant",
            Messages: [new LlmMessage("user", "hi")]
        ));

        var body = captured.ToString();
        body.ShouldContain("\"system\":", Case.Sensitive);
        body.ShouldContain("you are an assistant", Case.Sensitive);
        // Guards against PascalCase regression — Anthropic ignores unknown
        // fields silently, so the prompt would just disappear without 4xx.
        body.ShouldNotContain("\"System\":", Case.Sensitive);
    }

    [Fact]
    public async Task Sends_lowercase_messages_field()
    {
        var (sut, captured) = MakeSut();

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "hello world")]
        ));

        var body = captured.ToString();
        body.ShouldContain("\"messages\":");
        body.ShouldContain("hello world");
    }

    [Fact]
    public async Task Sends_lowercase_max_tokens_field()
    {
        var (sut, captured) = MakeSut();

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "hi")],
            MaxTokens: 123
        ));

        var body = captured.ToString();
        body.ShouldContain("\"max_tokens\":123");
    }

    private static (AnthropicLlmProvider sut, StringBuilder captured) MakeSut()
    {
        var captured = new StringBuilder();
        var http = new HttpClient(new CapturingHandler(captured));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = "test-key-not-used"
            }).Build();
        return (new AnthropicLlmProvider(http, config), captured);
    }

    private sealed class CapturingHandler(StringBuilder captured) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null)
            {
                captured.Append(await request.Content.ReadAsStringAsync(ct));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "id": "msg_test",
                        "model": "claude-haiku-4-5",
                        "content": [{"type": "text", "text": "ok"}],
                        "usage": {"input_tokens": 1, "output_tokens": 1}
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
