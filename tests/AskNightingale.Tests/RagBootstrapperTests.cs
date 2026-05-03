using AskNightingale.Services.Embeddings;
using AskNightingale.Services.Rag;
using FakeItEasy;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace AskNightingale.Tests;

public class RagBootstrapperTests
{
    [Fact]
    public async Task Skips_work_when_store_already_populated()
    {
        var embedder = A.Fake<IEmbeddingProvider>();
        var store = new InMemoryVectorStore();
        await store.AddAsync([(MakeChunk(0, "preexisting"), new float[] { 1, 0 })]);

        var sut = MakeBootstrapper(embedder, store, "doesnt-exist.txt", "wont-be-used.json");

        await sut.EnsureLoadedAsync();

        A.CallTo(() => embedder.EmbedAsync(
                A<IReadOnlyList<string>>._, A<EmbeddingPurpose>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        store.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Loads_from_persistence_when_file_exists()
    {
        var path = TempPath();
        try
        {
            // Pre-write a persistence file with one entry.
            var precursor = new InMemoryVectorStore();
            await precursor.AddAsync([(MakeChunk(0, "ventilation"), new float[] { 0.5f, 0.5f })]);
            await precursor.SaveToAsync(path);

            var embedder = A.Fake<IEmbeddingProvider>();
            var store = new InMemoryVectorStore();
            var sut = MakeBootstrapper(embedder, store, "doesnt-exist.txt", path);

            await sut.EnsureLoadedAsync();

            store.Count.ShouldBe(1);
            A.CallTo(() => embedder.EmbedAsync(
                    A<IReadOnlyList<string>>._, A<EmbeddingPurpose>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }
        finally { CleanUp(path); }
    }

    [Fact]
    public async Task Embeds_corpus_and_persists_when_no_persistence_file()
    {
        var corpusPath = TempPath(".txt");
        var persistPath = TempPath();
        try
        {
            // 5000 chars => 3 chunks at default Chunker settings (size 2000, overlap 200).
            await File.WriteAllTextAsync(corpusPath, new string('a', 5000));

            var embedder = A.Fake<IEmbeddingProvider>();
            A.CallTo(() => embedder.EmbedAsync(
                    A<IReadOnlyList<string>>._, EmbeddingPurpose.Document, A<CancellationToken>._))
                .ReturnsLazily(call =>
                {
                    var texts = (IReadOnlyList<string>)call.Arguments[0]!;
                    return Task.FromResult<IReadOnlyList<float[]>>(
                        texts.Select(_ => new float[] { 1, 0, 0 }).ToArray());
                });

            var store = new InMemoryVectorStore();
            var sut = MakeBootstrapper(embedder, store, corpusPath, persistPath);

            await sut.EnsureLoadedAsync();

            store.Count.ShouldBe(3);
            A.CallTo(() => embedder.EmbedAsync(
                    A<IReadOnlyList<string>>._, EmbeddingPurpose.Document, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
            File.Exists(persistPath).ShouldBeTrue();
        }
        finally { CleanUp(corpusPath); CleanUp(persistPath); }
    }

    [Fact]
    public async Task Strips_gutenberg_boilerplate_before_chunking()
    {
        var corpusPath = TempPath(".txt");
        var persistPath = TempPath();
        try
        {
            var corpus = """
                Some Project Gutenberg header text here that should be stripped.

                *** START OF THIS PROJECT GUTENBERG EBOOK NOTES ON NURSING ***

                The actual book content begins here. Florence Nightingale wrote about ventilation.

                *** END OF THIS PROJECT GUTENBERG EBOOK NOTES ON NURSING ***

                Some footer text that should also be stripped.
                """;
            await File.WriteAllTextAsync(corpusPath, corpus);

            IReadOnlyList<string>? capturedTexts = null;
            var embedder = A.Fake<IEmbeddingProvider>();
            A.CallTo(() => embedder.EmbedAsync(
                    A<IReadOnlyList<string>>._, A<EmbeddingPurpose>._, A<CancellationToken>._))
                .ReturnsLazily(call =>
                {
                    capturedTexts = (IReadOnlyList<string>)call.Arguments[0]!;
                    return Task.FromResult<IReadOnlyList<float[]>>(
                        capturedTexts.Select(_ => new float[] { 1, 0 }).ToArray());
                });

            var store = new InMemoryVectorStore();
            var sut = MakeBootstrapper(embedder, store, corpusPath, persistPath);

            await sut.EnsureLoadedAsync();

            capturedTexts.ShouldNotBeNull();
            var combined = string.Concat(capturedTexts!);
            combined.ShouldContain("Florence Nightingale wrote");
            combined.ShouldNotContain("Project Gutenberg header");
            combined.ShouldNotContain("footer text");
            combined.ShouldNotContain("*** START");
            combined.ShouldNotContain("*** END");
        }
        finally { CleanUp(corpusPath); CleanUp(persistPath); }
    }

    private static RagBootstrapper MakeBootstrapper(
        IEmbeddingProvider embedder,
        InMemoryVectorStore store,
        string corpusPath,
        string persistPath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG_CORPUS_PATH"] = corpusPath,
                ["RAG_STORE_PATH"] = persistPath
            })
            .Build();
        return new RagBootstrapper(new Chunker(), embedder, store, config);
    }

    private static Chunk MakeChunk(int index, string text) => new(index, text, 0);

    private static string TempPath(string ext = ".json") =>
        Path.Combine(Path.GetTempPath(), $"rag-{Guid.NewGuid():N}{ext}");

    private static void CleanUp(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
