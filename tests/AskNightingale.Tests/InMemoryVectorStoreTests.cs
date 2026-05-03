using AskNightingale.Services.Rag;
using Shouldly;

namespace AskNightingale.Tests;

public class InMemoryVectorStoreTests
{
    [Fact]
    public async Task Empty_store_returns_empty_top_k()
    {
        var sut = new InMemoryVectorStore();

        var result = await sut.GetTopKAsync([1, 0, 0], k: 5);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Adds_increase_count()
    {
        var sut = new InMemoryVectorStore();

        await sut.AddAsync([(MakeChunk(0, "a"), new float[] { 1, 0 })]);
        await sut.AddAsync([(MakeChunk(1, "b"), new float[] { 0, 1 })]);

        sut.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Cosine_returns_one_for_identical_vectors()
    {
        var sut = new InMemoryVectorStore();
        await sut.AddAsync([(MakeChunk(0, "a"), new float[] { 1, 0 })]);

        var result = await sut.GetTopKAsync([1, 0], k: 1);

        result[0].Score.ShouldBe(1f, tolerance: 0.001f);
    }

    [Fact]
    public async Task Cosine_returns_zero_for_orthogonal_vectors()
    {
        var sut = new InMemoryVectorStore();
        await sut.AddAsync([(MakeChunk(0, "a"), new float[] { 1, 0 })]);

        var result = await sut.GetTopKAsync([0, 1], k: 1);

        result[0].Score.ShouldBe(0f, tolerance: 0.001f);
    }

    [Fact]
    public async Task Cosine_returns_negative_one_for_opposite_vectors()
    {
        var sut = new InMemoryVectorStore();
        await sut.AddAsync([(MakeChunk(0, "a"), new float[] { 1, 0 })]);

        var result = await sut.GetTopKAsync([-1, 0], k: 1);

        result[0].Score.ShouldBe(-1f, tolerance: 0.001f);
    }

    [Fact]
    public async Task Cosine_returns_known_intermediate_value()
    {
        // [3,4] and [4,3] -> dot=24, |a|=|b|=5 -> 24/25 = 0.96
        var sut = new InMemoryVectorStore();
        await sut.AddAsync([(MakeChunk(0, "a"), new float[] { 4, 3 })]);

        var result = await sut.GetTopKAsync([3, 4], k: 1);

        result[0].Score.ShouldBe(0.96f, tolerance: 0.001f);
    }

    [Fact]
    public async Task Top_k_orders_by_descending_score()
    {
        var sut = new InMemoryVectorStore();
        await sut.AddAsync([
            (MakeChunk(0, "far"),   new float[] { 0, 1 }),
            (MakeChunk(1, "near"),  new float[] { 1, 0.1f }),
            (MakeChunk(2, "exact"), new float[] { 1, 0 })
        ]);

        var result = await sut.GetTopKAsync([1, 0], k: 3);

        result[0].Chunk.Index.ShouldBe(2); // exact match wins
        result[1].Chunk.Index.ShouldBe(1); // near
        result[2].Chunk.Index.ShouldBe(0); // far
        result[0].Score.ShouldBeGreaterThan(result[1].Score);
        result[1].Score.ShouldBeGreaterThan(result[2].Score);
    }

    [Fact]
    public async Task Top_k_caps_at_count_when_k_exceeds_entries()
    {
        var sut = new InMemoryVectorStore();
        await sut.AddAsync([(MakeChunk(0, "a"), new float[] { 1, 0 })]);

        var result = await sut.GetTopKAsync([1, 0], k: 10);

        result.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Mismatched_vector_dimensions_throws()
    {
        var sut = new InMemoryVectorStore();
        await sut.AddAsync([(MakeChunk(0, "a"), new float[] { 1, 0, 0 })]);

        await Should.ThrowAsync<ArgumentException>(
            () => sut.GetTopKAsync([1, 0], k: 1));
    }

    [Fact]
    public async Task Persistence_round_trip_preserves_entries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vec-{Guid.NewGuid():N}.json");
        try
        {
            var save = new InMemoryVectorStore();
            await save.AddAsync([
                (MakeChunk(0, "alpha"), new float[] { 0.1f, 0.2f, 0.3f }),
                (MakeChunk(1, "beta"),  new float[] { 0.4f, 0.5f, 0.6f })
            ]);
            await save.SaveToAsync(path);

            var load = new InMemoryVectorStore();
            await load.LoadFromAsync(path);

            load.Count.ShouldBe(2);
            var result = await load.GetTopKAsync([0.1f, 0.2f, 0.3f], k: 1);
            result[0].Chunk.Index.ShouldBe(0);
            result[0].Chunk.Text.ShouldBe("alpha");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static Chunk MakeChunk(int index, string text) => new(index, text, 0);
}
