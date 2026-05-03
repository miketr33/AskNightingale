using AskNightingale.Services;
using AskNightingale.Services.Embeddings;
using AskNightingale.Services.Llm;
using AskNightingale.Services.Rag;
using FakeItEasy;
using Shouldly;

namespace AskNightingale.Tests;

public class LlmChatServiceTests
{
    [Fact]
    public async Task Embeds_user_message_as_query_purpose()
    {
        var (sut, llm, embedder, store) = MakeSut();
        StubEmbedder(embedder);
        StubStore(store);
        StubLlm(llm, "any reply");

        await sut.RespondAsync("ventilation question");

        A.CallTo(() => embedder.EmbedAsync(
                A<IReadOnlyList<string>>.That.Matches(t => t.Count == 1 && t[0] == "ventilation question"),
                EmbeddingPurpose.Query,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Retrieves_top_4_from_store_with_query_embedding()
    {
        var (sut, llm, embedder, store) = MakeSut();
        var queryVec = new float[] { 0.1f, 0.2f };
        A.CallTo(() => embedder.EmbedAsync(A<IReadOnlyList<string>>._, EmbeddingPurpose.Query, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<float[]>>([queryVec]));
        StubStore(store);
        StubLlm(llm, "any reply");

        await sut.RespondAsync("hello");

        A.CallTo(() => store.GetTopKAsync(queryVec, 4, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task System_prompt_contains_retrieved_chunk_text()
    {
        var (sut, llm, embedder, store) = MakeSut();
        StubEmbedder(embedder);
        A.CallTo(() => store.GetTopKAsync(A<float[]>._, A<int>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<RetrievalResult>>([
                new RetrievalResult(MakeChunk(7, "ventilation requires fresh air"), 0.9f),
                new RetrievalResult(MakeChunk(12, "noise should be minimised"), 0.7f)
            ]));
        StubLlm(llm, "any reply");

        await sut.RespondAsync("anything");

        A.CallTo(() => llm.CompleteAsync(
                A<LlmRequest>.That.Matches(r =>
                    r.SystemPrompt != null &&
                    r.SystemPrompt.Contains("ventilation requires fresh air") &&
                    r.SystemPrompt.Contains("noise should be minimised")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task User_message_is_passed_through_unchanged()
    {
        var (sut, llm, embedder, store) = MakeSut();
        StubEmbedder(embedder);
        StubStore(store);
        StubLlm(llm, "ok");

        await sut.RespondAsync("what about ventilation?");

        A.CallTo(() => llm.CompleteAsync(
                A<LlmRequest>.That.Matches(r =>
                    r.Messages.Count == 1 &&
                    r.Messages[0].Role == "user" &&
                    r.Messages[0].Content == "what about ventilation?"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Returns_text_from_llm_response()
    {
        var (sut, llm, embedder, store) = MakeSut();
        StubEmbedder(embedder);
        StubStore(store);
        StubLlm(llm, "Florence says ventilation matters");

        var result = await sut.RespondAsync("anything");

        result.ShouldBe("Florence says ventilation matters");
    }

    [Fact]
    public async Task Cancellation_token_forwards_to_all_three_calls()
    {
        var (sut, llm, embedder, store) = MakeSut();
        StubEmbedder(embedder);
        StubStore(store);
        StubLlm(llm, "ok");
        using var cts = new CancellationTokenSource();

        await sut.RespondAsync("hello", cts.Token);

        A.CallTo(() => embedder.EmbedAsync(A<IReadOnlyList<string>>._, A<EmbeddingPurpose>._, cts.Token))
            .MustHaveHappened();
        A.CallTo(() => store.GetTopKAsync(A<float[]>._, A<int>._, cts.Token))
            .MustHaveHappened();
        A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, cts.Token))
            .MustHaveHappened();
    }

    private static (LlmChatService sut, ILlmProvider llm, IEmbeddingProvider embedder, IVectorStore store) MakeSut()
    {
        var llm = A.Fake<ILlmProvider>();
        var embedder = A.Fake<IEmbeddingProvider>();
        var store = A.Fake<IVectorStore>();
        return (new LlmChatService(llm, embedder, store), llm, embedder, store);
    }

    private static void StubEmbedder(IEmbeddingProvider embedder) =>
        A.CallTo(() => embedder.EmbedAsync(
                A<IReadOnlyList<string>>._, A<EmbeddingPurpose>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<float[]>>([new float[] { 1, 0 }]));

    private static void StubStore(IVectorStore store) =>
        A.CallTo(() => store.GetTopKAsync(A<float[]>._, A<int>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<RetrievalResult>>([]));

    private static void StubLlm(ILlmProvider llm, string reply) =>
        A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, A<CancellationToken>._))
            .Returns(new LlmResponse(reply, "model", 0, 0));

    private static Chunk MakeChunk(int index, string text) => new(index, text, 0);
}
