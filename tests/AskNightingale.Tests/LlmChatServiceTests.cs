using AskNightingale.Services;
using AskNightingale.Services.Llm;
using FakeItEasy;
using Shouldly;

namespace AskNightingale.Tests;

public class LlmChatServiceTests
{
    [Fact]
    public async Task Returns_text_from_llm_provider()
    {
        var llm = A.Fake<ILlmProvider>();
        A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, A<CancellationToken>._))
            .Returns(new LlmResponse("Florence Nightingale was born in 1820.", "claude-haiku-4-5", 10, 12));

        var sut = new LlmChatService(llm);

        var result = await sut.RespondAsync("Tell me about Nightingale");

        result.ShouldBe("Florence Nightingale was born in 1820.");
    }

    [Fact]
    public async Task Forwards_user_message_as_single_user_role_message()
    {
        var llm = A.Fake<ILlmProvider>();
        A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, A<CancellationToken>._))
            .Returns(new LlmResponse("ok", "model", 0, 0));

        var sut = new LlmChatService(llm);
        await sut.RespondAsync("hello");

        A.CallTo(() => llm.CompleteAsync(
            A<LlmRequest>.That.Matches(r =>
                r.Messages.Count == 1 &&
                r.Messages[0].Role == "user" &&
                r.Messages[0].Content == "hello"),
            A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded()
    {
        var llm = A.Fake<ILlmProvider>();
        A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, A<CancellationToken>._))
            .Returns(new LlmResponse("ok", "model", 0, 0));
        var sut = new LlmChatService(llm);
        using var cts = new CancellationTokenSource();

        await sut.RespondAsync("hello", cts.Token);

        A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, cts.Token)).MustHaveHappened();
    }
}
