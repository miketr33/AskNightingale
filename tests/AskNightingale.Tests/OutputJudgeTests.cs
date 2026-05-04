using AskNightingale.Services.Guardrails;
using AskNightingale.Services.Llm;
using FakeItEasy;
using Shouldly;

namespace AskNightingale.Tests;

public class OutputJudgeTests
{
    [Fact]
    public async Task Returns_approved_when_response_is_APPROVE()
    {
        var sut = MakeSut(judgeReply: "APPROVE");

        var verdict = await sut.VerifyAsync("q", "c", "a");

        verdict.Approved.ShouldBeTrue();
        verdict.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task Returns_refused_with_reason_when_response_starts_with_REFUSE()
    {
        var sut = MakeSut(judgeReply: "REFUSE: answer fabricated a quote");

        var verdict = await sut.VerifyAsync("q", "c", "a");

        verdict.Approved.ShouldBeFalse();
        verdict.Reason.ShouldBe("answer fabricated a quote");
    }

    [Fact]
    public async Task Treats_malformed_response_as_refusal()
    {
        var sut = MakeSut(judgeReply: "hmm, not sure about this one");

        var verdict = await sut.VerifyAsync("q", "c", "a");

        verdict.Approved.ShouldBeFalse();
        verdict.Reason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Approve_match_is_case_insensitive()
    {
        var sut = MakeSut(judgeReply: "approve");

        var verdict = await sut.VerifyAsync("q", "c", "a");

        verdict.Approved.ShouldBeTrue();
    }

    [Fact]
    public async Task Strips_whitespace_around_response()
    {
        var sut = MakeSut(judgeReply: "\n  APPROVE  \n");

        var verdict = await sut.VerifyAsync("q", "c", "a");

        verdict.Approved.ShouldBeTrue();
    }

    [Fact]
    public async Task Sends_question_context_and_answer_in_prompt()
    {
        var llm = A.Fake<ILlmProvider>();
        StubJudgeReply(llm, "APPROVE");
        var sut = new OutputJudge(llm);

        await sut.VerifyAsync("the question text", "the context text", "the answer text");

        A.CallTo(() => llm.CompleteAsync(
                A<LlmRequest>.That.Matches(r =>
                    r.Messages.Count == 1 &&
                    r.Messages[0].Role == "user" &&
                    r.Messages[0].Content.Contains("the question text") &&
                    r.Messages[0].Content.Contains("the context text") &&
                    r.Messages[0].Content.Contains("the answer text")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Limits_max_tokens_to_keep_judge_response_short()
    {
        var llm = A.Fake<ILlmProvider>();
        StubJudgeReply(llm, "APPROVE");
        var sut = new OutputJudge(llm);

        await sut.VerifyAsync("q", "c", "a");

        A.CallTo(() => llm.CompleteAsync(
                A<LlmRequest>.That.Matches(r => r.MaxTokens == 200),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded()
    {
        var llm = A.Fake<ILlmProvider>();
        StubJudgeReply(llm, "APPROVE");
        var sut = new OutputJudge(llm);
        using var cts = new CancellationTokenSource();

        await sut.VerifyAsync("q", "c", "a", cts.Token);

        A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, cts.Token))
            .MustHaveHappened();
    }

    private static OutputJudge MakeSut(string judgeReply)
    {
        var llm = A.Fake<ILlmProvider>();
        StubJudgeReply(llm, judgeReply);
        return new OutputJudge(llm);
    }

    private static void StubJudgeReply(ILlmProvider llm, string reply) =>
        A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, A<CancellationToken>._))
            .Returns(new LlmResponse(reply, "model", 0, 0));
}
