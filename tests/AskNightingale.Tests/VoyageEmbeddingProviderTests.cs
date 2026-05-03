using AskNightingale.Services.Embeddings;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace AskNightingale.Tests;

public class VoyageEmbeddingProviderTests
{
    [Fact]
    public void Throws_clear_error_when_api_key_missing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        using var http = new HttpClient();

        var ex = Should.Throw<InvalidOperationException>(
            () => new VoyageEmbeddingProvider(http, config));

        ex.Message.ShouldContain("VOYAGE_API_KEY");
    }

    [Fact]
    public async Task Empty_input_returns_empty_list_without_calling_api()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VOYAGE_API_KEY"] = "no-real-key-needed-no-call-made"
            })
            .Build();
        using var http = new HttpClient();  // never actually called for empty input
        var sut = new VoyageEmbeddingProvider(http, config);

        var result = await sut.EmbedAsync([], EmbeddingPurpose.Document);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Round_trip_against_live_voyage_api()
    {
        var apiKey = Environment.GetEnvironmentVariable("VOYAGE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) return;  // skip silently in CI without key

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VOYAGE_API_KEY"] = apiKey
            })
            .Build();
        using var http = new HttpClient();
        var sut = new VoyageEmbeddingProvider(http, config);

        var docs = await sut.EmbedAsync(
            ["ventilation in a sickroom", "the importance of cleanliness"],
            EmbeddingPurpose.Document);

        docs.Count.ShouldBe(2);
        docs[0].Length.ShouldBeGreaterThan(0);
        docs[1].Length.ShouldBe(docs[0].Length);  // same model => same vector dimension

        var queries = await sut.EmbedAsync(
            ["how should fresh air be managed?"],
            EmbeddingPurpose.Query);

        queries.Count.ShouldBe(1);
        queries[0].Length.ShouldBe(docs[0].Length);
    }
}
