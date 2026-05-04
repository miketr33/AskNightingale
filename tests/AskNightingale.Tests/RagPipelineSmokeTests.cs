using AskNightingale.Services;
using AskNightingale.Services.Embeddings;
using AskNightingale.Services.Guardrails;
using AskNightingale.Services.Llm;
using AskNightingale.Services.Rag;
using FakeItEasy;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace AskNightingale.Tests;

// End-to-end "does the pipeline plug together" check using REAL Chunker
// and REAL InMemoryVectorStore — only the embedder and LLM are faked.
// One test, kept deliberately simple. The fakes return deterministic
// outputs so we can assert the prompt actually carries retrieved context.
public class RagPipelineSmokeTests
{
    [Fact]
    public async Task Bootstrapper_then_chat_service_runs_end_to_end()
    {
        var corpusPath = TempPath(".txt");
        var persistPath = TempPath();
        try
        {
            const string corpus =
                "Florence Nightingale wrote about ventilation in 1859. " +
                "Fresh air is essential for sickrooms. " +
                "Patients need cleanliness and quiet. Noise harms recovery.";
            await File.WriteAllTextAsync(corpusPath, corpus);

            var embedder = A.Fake<IEmbeddingProvider>();
            A.CallTo(() => embedder.EmbedAsync(
                    A<IReadOnlyList<string>>._, A<EmbeddingPurpose>._, A<CancellationToken>._))
                .ReturnsLazily(call =>
                {
                    var texts = (IReadOnlyList<string>)call.Arguments[0]!;
                    // Trivial deterministic embedding: text length on dim 0.
                    return Task.FromResult<IReadOnlyList<float[]>>(
                        texts.Select(t => new float[] { t.Length, 0 }).ToArray());
                });

            var llm = A.Fake<ILlmProvider>();
            A.CallTo(() => llm.CompleteAsync(A<LlmRequest>._, A<CancellationToken>._))
                .Returns(new LlmResponse("answer using context", "model", 0, 0));

            var store = new InMemoryVectorStore();
            var chunker = new Chunker();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RAG_CORPUS_PATH"] = corpusPath,
                    ["RAG_STORE_PATH"] = persistPath
                })
                .Build();
            var bootstrapper = new RagBootstrapper(chunker, embedder, store, config);
            await bootstrapper.EnsureLoadedAsync();

            var retrievalGuard = new RetrievalGuard(config);
            var inputGuard = new InputGuard();
            var chatService = new LlmChatService(llm, embedder, store, retrievalGuard, inputGuard);
            var result = await chatService.RespondAsync("tell me about ventilation");

            result.Content.ShouldBe("answer using context");
            result.Citations.Count.ShouldBeGreaterThan(0);
            store.Count.ShouldBeGreaterThan(0);

            // The system prompt sent to the LLM should include corpus content.
            A.CallTo(() => llm.CompleteAsync(
                    A<LlmRequest>.That.Matches(r =>
                        r.SystemPrompt != null && r.SystemPrompt.Contains("Florence")),
                    A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }
        finally { CleanUp(corpusPath); CleanUp(persistPath); }
    }

    private static string TempPath(string ext = ".json") =>
        Path.Combine(Path.GetTempPath(), $"smoke-{Guid.NewGuid():N}{ext}");

    private static void CleanUp(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
