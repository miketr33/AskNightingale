using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AskNightingale.Services.Llm;

public class BedrockLlmProvider(IAmazonBedrockRuntime client, IConfiguration config) : ILlmProvider
{
    private readonly string _modelId = config["BEDROCK_MODEL_ID"]
                                       ?? throw new InvalidOperationException(
                                           "BEDROCK_MODEL_ID is not configured. Set it in .env (e.g. eu.anthropic.claude-haiku-4-5-20251001-v1:0).");

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var converse = new ConverseRequest
        {
            ModelId = _modelId,
            Messages = request.Messages
                .Select(m => new Message
                {
                    Role = m.Role == "user" ? ConversationRole.User : ConversationRole.Assistant,
                    Content = [new ContentBlock { Text = m.Content }]
                })
                .ToList(),
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = request.MaxTokens ?? 500
            }
        };

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            converse.System = [new SystemContentBlock { Text = request.SystemPrompt }];
        }

        var response = await client.ConverseAsync(converse, ct);

        var text = string.Concat(
            response.Output.Message.Content
                .Where(c => c.Text is not null)
                .Select(c => c.Text));

        return new LlmResponse(
            Content: text,
            Model: _modelId,
            InputTokens: response.Usage.InputTokens ?? 0,
            OutputTokens: response.Usage.OutputTokens ?? 0);
    }
}