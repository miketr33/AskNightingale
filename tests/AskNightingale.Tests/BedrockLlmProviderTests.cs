using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AskNightingale.Services.Llm;
using FakeItEasy;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace AskNightingale.Tests;

public class BedrockLlmProviderTests
{
    [Fact]
    public void Throws_clear_error_when_model_id_missing()
    {
        var client = A.Fake<IAmazonBedrockRuntime>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var ex = Should.Throw<InvalidOperationException>(
            () => new BedrockLlmProvider(client, config));

        ex.Message.ShouldContain("BEDROCK_MODEL_ID");
    }

    [Fact]
    public async Task Sends_user_message_in_converse_request()
    {
        var (sut, client) = MakeSut();
        StubResponse(client, "any reply");

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "what about ventilation?")]
        ));

        A.CallTo(() => client.ConverseAsync(
                A<ConverseRequest>.That.Matches(r =>
                    r.Messages.Count == 1 &&
                    r.Messages[0].Role == ConversationRole.User &&
                    r.Messages[0].Content[0].Text == "what about ventilation?"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Sends_system_prompt_when_provided()
    {
        var (sut, client) = MakeSut();
        StubResponse(client, "any reply");

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: "you are an assistant",
            Messages: [new LlmMessage("user", "hi")]
        ));

        A.CallTo(() => client.ConverseAsync(
                A<ConverseRequest>.That.Matches(r =>
                    r.System != null &&
                    r.System.Count == 1 &&
                    r.System[0].Text == "you are an assistant"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Omits_system_when_prompt_is_null_or_empty()
    {
        var (sut, client) = MakeSut();
        StubResponse(client, "any reply");

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "hi")]
        ));

        A.CallTo(() => client.ConverseAsync(
                A<ConverseRequest>.That.Matches(r => r.System == null || r.System.Count == 0),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Forwards_max_tokens_to_inference_config()
    {
        var (sut, client) = MakeSut();
        StubResponse(client, "any reply");

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "hi")],
            MaxTokens: 123
        ));

        A.CallTo(() => client.ConverseAsync(
                A<ConverseRequest>.That.Matches(r => r.InferenceConfig.MaxTokens == 123),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Defaults_max_tokens_when_request_omits_it()
    {
        var (sut, client) = MakeSut();
        StubResponse(client, "any reply");

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "hi")]
        ));

        A.CallTo(() => client.ConverseAsync(
                A<ConverseRequest>.That.Matches(r => r.InferenceConfig.MaxTokens == 500),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Returns_text_and_token_usage_from_response()
    {
        var (sut, client) = MakeSut();
        StubResponse(client, "Florence says ventilation matters", inputTokens: 10, outputTokens: 8);

        var result = await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "hi")]
        ));

        result.Content.ShouldBe("Florence says ventilation matters");
        result.InputTokens.ShouldBe(10);
        result.OutputTokens.ShouldBe(8);
        result.Model.ShouldBe("test.model.id");
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded()
    {
        var (sut, client) = MakeSut();
        StubResponse(client, "ok");
        using var cts = new CancellationTokenSource();

        await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: null,
            Messages: [new LlmMessage("user", "hi")]
        ), cts.Token);

        A.CallTo(() => client.ConverseAsync(A<ConverseRequest>._, cts.Token))
            .MustHaveHappened();
    }

    [Fact]
    public async Task Round_trip_against_live_bedrock_api()
    {
        var modelId = Environment.GetEnvironmentVariable("BEDROCK_MODEL_ID");
        var region = Environment.GetEnvironmentVariable("AWS_REGION");
        if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(region))
        {
            return; // skip silently when not configured (CI, fresh checkout)
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BEDROCK_MODEL_ID"] = modelId
            })
            .Build();
        using var client = new AmazonBedrockRuntimeClient(
            Amazon.RegionEndpoint.GetBySystemName(region));
        var sut = new BedrockLlmProvider(client, config);

        var response = await sut.CompleteAsync(new LlmRequest(
            SystemPrompt: "Reply with exactly the word 'pong'. Nothing else.",
            Messages: [new LlmMessage("user", "ping")],
            MaxTokens: 20
        ));

        response.Content.ShouldContain("pong", Case.Insensitive);
        response.OutputTokens.ShouldBeGreaterThan(0);
    }

    private static (BedrockLlmProvider sut, IAmazonBedrockRuntime client) MakeSut()
    {
        var client = A.Fake<IAmazonBedrockRuntime>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BEDROCK_MODEL_ID"] = "test.model.id"
            })
            .Build();
        return (new BedrockLlmProvider(client, config), client);
    }

    private static void StubResponse(
        IAmazonBedrockRuntime client, string text, int inputTokens = 0, int outputTokens = 0)
    {
        A.CallTo(() => client.ConverseAsync(A<ConverseRequest>._, A<CancellationToken>._))
            .Returns(new ConverseResponse
            {
                Output = new ConverseOutput
                {
                    Message = new Message
                    {
                        Role = ConversationRole.Assistant,
                        Content = [new ContentBlock { Text = text }]
                    }
                },
                Usage = new TokenUsage
                {
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                }
            });
    }
}
