using AskNightingale.Services.Rag;
using Shouldly;

namespace AskNightingale.Tests;

public class ChunkerTests
{
    [Fact]
    public void Empty_text_returns_no_chunks()
    {
        new Chunker().Split("").ShouldBeEmpty();
    }

    [Fact]
    public void Whitespace_only_returns_no_chunks()
    {
        new Chunker().Split("   \n\t  ").ShouldBeEmpty();
    }

    [Fact]
    public void Short_text_fits_in_single_chunk()
    {
        var text = new string('a', 1000);

        var result = new Chunker().Split(text);

        result.Count.ShouldBe(1);
        result[0].Text.ShouldBe(text);
        result[0].StartCharOffset.ShouldBe(0);
        result[0].Index.ShouldBe(0);
    }

    [Fact]
    public void Text_exactly_chunk_size_is_a_single_chunk()
    {
        var text = new string('b', 2000);

        new Chunker().Split(text).Count.ShouldBe(1);
    }

    [Fact]
    public void Text_just_over_chunk_size_produces_two_chunks()
    {
        var text = new string('c', 2050);

        new Chunker().Split(text).Count.ShouldBe(2);
    }

    [Fact]
    public void Consecutive_chunks_overlap_by_configured_amount()
    {
        var sut = new Chunker(chunkSize: 100, overlap: 20);
        var text = new string('x', 250);

        var result = sut.Split(text);

        // step = chunkSize - overlap = 80
        result[1].StartCharOffset.ShouldBe(80);
        result[2].StartCharOffset.ShouldBe(160);
    }

    [Fact]
    public void Chunk_indexes_are_sequential_from_zero()
    {
        var sut = new Chunker(chunkSize: 100, overlap: 20);
        var text = new string('x', 500);

        var result = sut.Split(text);

        result.Select(c => c.Index).ShouldBe(Enumerable.Range(0, result.Count));
    }

    [Fact]
    public void Final_chunk_contains_tail_of_text()
    {
        var sut = new Chunker(chunkSize: 100, overlap: 20);
        var text = new string('x', 250) + "END";

        var result = sut.Split(text);

        result[^1].Text.ShouldEndWith("END");
    }

    [Fact]
    public void Invalid_overlap_throws()
    {
        var sut = new Chunker(chunkSize: 100, overlap: 100);

        Should.Throw<ArgumentOutOfRangeException>(() => sut.Split("anything goes here"));
    }
}
